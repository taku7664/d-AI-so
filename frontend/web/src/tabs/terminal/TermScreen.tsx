// 방 화면 하나: xterm.js + /api/terminal/rooms/{id}/pty. 방마다 새로 만든다(key=room.id). 방을 바꿀 때 옛 화면 위에 그리지 않는다
// 옛 앱(old/src/Daiso.App/Assets/xterm/index.html)에서 고친 것을 그대로 지킨다
//   - 출력은 바이트 그대로 term.write(Uint8Array). 글자 중간에서 잘린 덩어리는 xterm 이 이어 붙인다
//   - 맞추기(fit)는 40ms 늦춰 한 번만. 크기가 같으면 서버도 안 보낸다(줄 겹침)
//   - 스크롤바 폭을 늘 잡아 둔다. 안 그러면 스크롤백이 생길 때 열 수가 바뀌어 다시 그린다(옛 앱 eb521fa)
//   - Ctrl+휠은 xterm 의 휠 훅에서 받는다. document 에 걸면 뷰포트가 먼저 먹는다(옛 앱 2adb131)
//   - 붙여넣기에 글이 없고 파일·그림만 있으면 xterm 에 넘기지 않고 방에 붙인다(attach.ts). 그림만 있으면 CLI 가 클립보드에서 읽는다
import '@xterm/xterm/css/xterm.css';
import { FitAddon } from '@xterm/addon-fit';
import { SearchAddon } from '@xterm/addon-search';
import { Unicode11Addon } from '@xterm/addon-unicode11';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { Terminal } from '@xterm/xterm';
import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from 'react';
import { t } from '../../strings';
import { clipboardHasImage, filesOf } from './attach';

export interface TermHandle {
  find: (query: string, backwards?: boolean) => boolean;
  clearFind: () => void;
  focus: () => void;
  /** 스크롤백까지 그린 글자. 오른쪽 빈칸은 지운다 */
  text: () => string;
  send: (data: string) => void;
  /** 입력 줄에 붙여 넣는다. bracketed paste 면 xterm 이 감싼다 */
  paste: (text: string) => void;
}

/** 앱 단축키는 xterm 이 먹지 않고 창으로 넘긴다 */
function appShortcut(event: KeyboardEvent): boolean {
  if (!event.ctrlKey || event.altKey) return false;
  return /^[1-6]$/.test(event.key) || event.key.toLowerCase() === 'p';
}

function cssVar(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

export const TermScreen = forwardRef<
  TermHandle,
  {
    roomId: string;
    onFind: () => void;
    onExit: (code: number | null) => void;
    /** 붙여넣기로 온 파일·그림 */
    onFiles: (files: File[]) => void;
    /** 오른쪽 클릭 붙이기에서 클립보드에 글은 없고 그림만 있다 */
    onClipboardImage: () => void;
  }
>(function TermScreen({ roomId, onFind, onExit, onFiles, onClipboardImage }, ref) {
  const box = useRef<HTMLDivElement>(null);
  const term = useRef<Terminal | null>(null);
  const search = useRef<SearchAddon | null>(null);
  const socket = useRef<WebSocket | null>(null);
  const handlers = useRef({ onFind, onExit, onFiles, onClipboardImage });
  useEffect(() => {
    handlers.current = { onFind, onExit, onFiles, onClipboardImage };
  });

  const send = useCallback((message: object) => {
    if (socket.current?.readyState === WebSocket.OPEN) socket.current.send(JSON.stringify(message));
  }, []);

  useImperativeHandle(ref, () => ({
    find: (query, backwards) =>
      (backwards ? search.current?.findPrevious(query) : search.current?.findNext(query)) ?? false,
    clearFind: () => search.current?.clearDecorations(),
    focus: () => term.current?.focus(),
    text: () => {
      const buffer = term.current?.buffer.active;
      if (!buffer) return '';
      const lines: string[] = [];
      for (let i = 0; i < buffer.length; i++) lines.push(buffer.getLine(i)?.translateToString(true) ?? '');
      return lines.join('\n').replace(/\n+$/, '');
    },
    send: (data) => send({ t: 'in', d: data }),
    paste: (text) => term.current?.paste(text),
  }));

  useEffect(() => {
    const element = box.current!;
    const terminal = new Terminal({
      fontFamily: cssVar('--f-mono') || 'Consolas, monospace',
      fontSize: 14,
      scrollback: 5000,
      allowProposedApi: true,
      cursorBlink: true,
      windowsPty: { backend: 'conpty' },
      theme: {
        background: cssVar('--term-bg'),
        foreground: cssVar('--term-fg'),
        cursor: cssVar('--term-fg'),
        selectionBackground: 'rgba(233, 189, 56, 0.35)',
      },
    });
    const fit = new FitAddon();
    const finder = new SearchAddon();
    terminal.loadAddon(fit);
    terminal.loadAddon(finder);
    terminal.loadAddon(new Unicode11Addon());
    terminal.unicode.activeVersion = '11';
    terminal.loadAddon(new WebLinksAddon((_, uri) => window.open(uri, '_blank')));
    terminal.open(element);
    term.current = terminal;
    search.current = finder;

    // 키: 선택이 있으면 Ctrl+C 는 복사, 없으면 프로세스로. Ctrl+V 는 xterm 이 키로 보내지 않고 브라우저 붙여넣기(paste 이벤트)에 맡긴다
    terminal.attachCustomKeyEventHandler((event) => {
      if (event.type !== 'keydown') return true;
      if (appShortcut(event)) return false;
      const key = event.key.toLowerCase();
      if (event.ctrlKey && !event.shiftKey && key === 'c' && terminal.hasSelection()) {
        void navigator.clipboard.writeText(terminal.getSelection());
        terminal.clearSelection();
        return false;
      }
      if ((event.ctrlKey && key === 'v') || (event.shiftKey && key === 'insert')) return false;
      if (event.ctrlKey && key === 'f') {
        handlers.current.onFind();
        return false;
      }
      return true;
    });

    // 글자 크기: Ctrl+휠, Ctrl+= / Ctrl+-
    terminal.attachCustomWheelEventHandler((event) => {
      if (!event.ctrlKey) return true;
      const size = Math.min(28, Math.max(8, (terminal.options.fontSize ?? 14) + (event.deltaY < 0 ? 1 : -1)));
      terminal.options.fontSize = size;
      fit.fit();
      return false;
    });

    // 오른쪽 클릭: 선택이 있으면 복사, 없으면 붙이기. 글이 없고 그림이 있으면 그림을 붙인다
    const onContext = (event: MouseEvent) => {
      event.preventDefault();
      if (terminal.hasSelection()) {
        void navigator.clipboard.writeText(terminal.getSelection());
        terminal.clearSelection();
        return;
      }
      void navigator.clipboard.readText().then(async (text) => {
        if (text) terminal.paste(text);
        else if (await clipboardHasImage()) handlers.current.onClipboardImage();
      });
    };
    element.addEventListener('contextmenu', onContext);

    // 붙여넣기: 글이 있으면 xterm 이 붙인다. 파일·그림만 있으면 xterm 보다 먼저 받아 방에 붙인다
    const onPaste = (event: ClipboardEvent) => {
      const files = filesOf(event.clipboardData);
      if (!files.length) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      handlers.current.onFiles(files);
    };
    element.addEventListener('paste', onPaste, true);

    const ws = new WebSocket(
      `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/api/terminal/rooms/${roomId}/pty`,
    );
    ws.binaryType = 'arraybuffer';
    socket.current = ws;
    ws.onopen = () => send({ t: 'resize', c: terminal.cols, r: terminal.rows });
    ws.onmessage = (event) => {
      if (event.data instanceof ArrayBuffer) {
        terminal.write(new Uint8Array(event.data));
        return;
      }
      try {
        const message = JSON.parse(event.data as string) as { t: string; code?: number | null };
        if (message.t === 'exit') {
          terminal.write(`\r\n\x1b[2m${t('terminal.exited', { code: message.code ?? '?' })}\x1b[0m\r\n`);
          handlers.current.onExit(message.code ?? null);
        }
      } catch {
        // 모르는 메시지는 버린다
      }
    };
    const input = terminal.onData((data) => send({ t: 'in', d: data }));

    let timer: number | undefined;
    const observer = new ResizeObserver(() => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        if (!element.isConnected || element.clientWidth === 0) return;
        fit.fit();
        send({ t: 'resize', c: terminal.cols, r: terminal.rows });
      }, 40);
    });
    observer.observe(element);
    fit.fit();
    terminal.focus();

    return () => {
      observer.disconnect();
      window.clearTimeout(timer);
      element.removeEventListener('contextmenu', onContext);
      element.removeEventListener('paste', onPaste, true);
      input.dispose();
      ws.close();
      terminal.dispose();
      term.current = null;
      socket.current = null;
    };
  }, [roomId, send]);

  return <div className="term" ref={box} />;
});

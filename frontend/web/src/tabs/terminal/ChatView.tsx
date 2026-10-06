// 말풍선 보기 (docs/design/terminal-chat.html). 겉은 말풍선, 속은 진짜 터미널: 말풍선은 CLI 가 쓰는 세션 기록에서 오고,
// 입력칸의 글은 그 방 CLI 의 입력 줄로 들어간다. 허락 요청과 화면이 있어야 하는 명령(/login 등)은 터미널 보기로 넘긴다
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../../icons/Icon';
import { t } from '../../strings';
import { ToolBadge, useTools } from '../../tools';
import { useCommands, useRoomChat, useSendToRoom, type ChatItem, type CommandGroup, type Room } from './api';
import { Md } from './Md';

type Block =
  | { kind: 'me'; item: ChatItem }
  | { kind: 'ai'; item: ChatItem }
  | { kind: 'tools'; key: number; calls: ChatItem[]; failed: number };

/** 이어진 도구 호출은 한 줄로 접는다. 도구 결과는 칸으로 보이지 않고 실패만 센다 */
function blocks(items: ChatItem[]): Block[] {
  const out: Block[] = [];
  for (const item of items) {
    if (item.kind === 'me' || item.kind === 'ai') {
      out.push(item.kind === 'me' ? { kind: 'me', item } : { kind: 'ai', item });
      continue;
    }
    const last = out[out.length - 1];
    const group = last?.kind === 'tools' ? last : null;
    if (item.kind === 'tool') {
      if (group) group.calls.push(item);
      else out.push({ kind: 'tools', key: item.seq, calls: [item], failed: 0 });
    } else if (item.kind === 'result' && item.error && group) {
      group.failed++;
    }
  }
  return out;
}

function hhmm(iso: string): string {
  const at = new Date(iso);
  return `${String(at.getHours()).padStart(2, '0')}:${String(at.getMinutes()).padStart(2, '0')}`;
}

export function ChatView({ room, onTerminal }: { room: Room; onTerminal: () => void }) {
  const chat = useRoomChat(room.id);
  const tools = useTools();
  const tool = tools.data?.find((x) => x.id === room.tool);
  const list = useMemo(() => blocks(chat.data?.items ?? []), [chat.data]);
  const box = useRef<HTMLDivElement>(null);
  const stick = useRef(true);
  const [open, setOpen] = useState<Set<number>>(new Set());

  // 맨 아래를 보고 있었으면 새 칸이 와도 맨 아래에 붙어 있는다. 위로 올려 읽는 중이면 건드리지 않는다
  useLayoutEffect(() => {
    if (stick.current && box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [list.length, room.state]);

  const name = tool?.title ?? room.tool;
  return (
    <div className="chat">
      <div
        className="msgs"
        ref={box}
        onScroll={(e) => {
          const el = e.currentTarget;
          stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
        }}
      >
        {chat.data && !chat.data.found && <div className="chat-empty">{t('chat.noTranscript')}</div>}
        {list.map((block) =>
          block.kind === 'me' ? (
            <div className="msg me" key={block.item.seq}>
              {block.item.text}
            </div>
          ) : block.kind === 'ai' ? (
            <div className="msg ai" key={block.item.seq}>
              <span className="who">
                {tool && <ToolBadge tool={tool} small />}
                {name} · {hhmm(block.item.at)}
              </span>
              <div className="body">
                <Md text={block.item.text} />
              </div>
            </div>
          ) : (
            <ToolGroup
              key={block.key}
              block={block}
              open={open.has(block.key)}
              onToggle={() =>
                setOpen((now) => {
                  const next = new Set(now);
                  if (next.has(block.key)) next.delete(block.key);
                  else next.add(block.key);
                  return next;
                })
              }
            />
          ),
        )}
        {room.state === 'run' && (
          <div className="working">
            <span className="dots">
              <span />
              <span />
              <span />
            </span>
            {t('chat.working', { tool: name })}
          </div>
        )}
        {room.state === 'ask' && (
          <div className="ask">
            <Icon name="hourglass-split" />
            <div>
              <b>{t('chat.ask', { tool: name })}</b>
              <small>{t('chat.askHint')}</small>
            </div>
            <button className="btn primary small" type="button" onClick={onTerminal}>
              <Icon name="terminal" />
              {t('chat.answerInTerminal')}
            </button>
          </div>
        )}
      </div>
      <Composer room={room} toolName={name} onTerminal={onTerminal} />
    </div>
  );
}

function ToolGroup({
  block,
  open,
  onToggle,
}: {
  block: Extract<Block, { kind: 'tools' }>;
  open: boolean;
  onToggle: () => void;
}) {
  const names = [...new Set(block.calls.map((call) => call.tool ?? ''))].join(' · ');
  return (
    <div className="toolgroup">
      <button type="button" onClick={onToggle} aria-expanded={open}>
        <Icon name={open ? 'chevron-down' : 'chevron-right'} />
        {t('chat.tools', { count: block.calls.length, names })}
        {block.failed > 0 && <span className="err">{t('chat.toolsFailed', { count: block.failed })}</span>}
      </button>
      {open && (
        <div className="list">
          {block.calls.map((call) => (
            <span className="one" key={call.seq} title={call.text}>
              <b>{call.tool}</b>
              <span className="ell">{call.text.slice((call.tool ?? '').length).trim()}</span>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

function Composer({ room, toolName, onTerminal }: { room: Room; toolName: string; onTerminal: () => void }) {
  const send = useSendToRoom();
  const commands = useCommands(room.tool, room.folder);
  const [draft, setDraft] = useState('');
  const [index, setIndex] = useState(0);
  const area = useRef<HTMLTextAreaElement>(null);
  const exited = room.state === 'exited';

  // / 로 시작하고 아직 띄어쓰기 전이면 목록을 띄운다
  const query = draft.startsWith('/') && !/\s/.test(draft) ? draft.slice(1).toLowerCase() : null;
  const groups: CommandGroup[] = useMemo(
    () =>
      query === null
        ? []
        : (commands.data ?? [])
            .map((group) => ({
              ...group,
              items: group.items.filter(
                (item) =>
                  item.name.slice(1).toLowerCase().includes(query) || item.description.toLowerCase().includes(query),
              ),
            }))
            .filter((group) => group.items.length),
    [commands.data, query],
  );
  const flat = groups.flatMap((group) => group.items);
  const pick = Math.min(index, Math.max(0, flat.length - 1));

  useEffect(() => {
    if (!area.current) return;
    area.current.style.height = 'auto';
    area.current.style.height = `${Math.min(area.current.scrollHeight, 160)}px`;
  }, [draft]);

  const submit = (text: string) => {
    if (!text.trim() || exited) return;
    send.mutate({ id: room.id, text }, { onSuccess: () => setDraft('') });
  };

  const choose = (name: string, terminal: boolean) => {
    if (terminal) {
      // 화면이 있어야 하는 명령은 보내고 터미널 보기로 넘어간다
      submit(name);
      onTerminal();
      return;
    }
    setDraft(`${name} `);
    area.current?.focus();
  };

  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.nativeEvent.isComposing) return;
    if (flat.length && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      e.preventDefault();
      setIndex((pick + (e.key === 'ArrowDown' ? 1 : -1) + flat.length) % flat.length);
      return;
    }
    const current = flat[pick];
    if (current && (e.key === 'Tab' || (e.key === 'Enter' && !e.shiftKey))) {
      e.preventDefault();
      choose(current.name, current.terminal);
      return;
    }
    if (query !== null && e.key === 'Escape') {
      setDraft('');
      return;
    }
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit(draft);
    }
  };

  let n = 0;
  return (
    <div className="composer">
      {query !== null && (
        <div className="cmdpop" role="listbox">
          {!flat.length && <div className="cmdgroup">{t('chat.noCommand')}</div>}
          {groups.map((group) => (
            <div key={group.kind + group.source}>
              <div className="cmdgroup">
                <Icon name={group.kind === 'builtin' ? 'box' : group.kind === 'user' ? 'person' : 'folder'} />
                {group.source}
              </div>
              {group.items.map((item) => {
                const k = n++;
                return (
                  <button
                    key={item.name}
                    className={`cmd ${item.terminal ? 'tui' : ''} ${k === pick ? 'on' : ''}`}
                    type="button"
                    role="option"
                    aria-selected={k === pick}
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => choose(item.name, item.terminal)}
                  >
                    <b>{item.name}</b>
                    <em>{item.argument}</em>
                    <span>
                      {item.description}
                      {item.terminal && ` · ${t('chat.inTerminal')}`}
                    </span>
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      )}
      <div className="compose-box">
        <textarea
          ref={area}
          rows={1}
          value={draft}
          disabled={exited}
          placeholder={exited ? t('chat.exited') : t('chat.placeholder', { tool: toolName })}
          aria-label={t('chat.placeholder', { tool: toolName })}
          onChange={(e) => {
            setDraft(e.target.value);
            setIndex(0);
          }}
          onKeyDown={onKey}
        />
        <button
          className="send"
          type="button"
          title={t('chat.send')}
          disabled={!draft.trim() || send.isPending || exited}
          onClick={() => submit(draft)}
        >
          <Icon name="send-fill" />
        </button>
      </div>
      <div className="compose-hint">
        <span>{t('chat.hintKeys')}</span>
        <span>{send.isError ? t('chat.sendFailed') : t('chat.hintWhere', { tool: toolName })}</span>
      </div>
    </div>
  );
}

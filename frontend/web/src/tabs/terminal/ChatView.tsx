// 말풍선 보기 (docs/design/terminal-chat.html). 겉은 말풍선, 속은 진짜 터미널: 말풍선은 CLI 가 쓰는 세션 기록에서 오고,
// 입력칸의 글은 그 방 CLI 의 입력 줄로 들어간다. 허락 요청과 화면이 있어야 하는 명령(/login 등)은 터미널 보기로 넘긴다
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../../icons/Icon';
import { t } from '../../strings';
import { ToolBadge, useTools } from '../../tools';
import { useCommands, useRoomChat, useSendToRoom, type ChatItem, type CommandGroup, type Room } from './api';
import { attachImages, filesOf, quotePaths, where, type Attachment } from './attach';
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

interface AttachProps {
  atts: Attachment[];
  /** 입력칸에 붙여 넣은 파일·그림 */
  onFiles: (files: File[]) => void;
  onPick: () => void;
  onRemove: (key: string) => void;
  /** 보냈다. 첨부를 비운다 */
  onSent: () => void;
}

export function ChatView({ room, onTerminal, ...attach }: { room: Room; onTerminal: () => void } & AttachProps) {
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
      <Composer room={room} toolName={name} onTerminal={onTerminal} {...attach} />
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

/** 입력칸 위 첨부 칩 하나. 그림은 미리보기와 크기, 파일은 어느 폴더인지 */
function Chip({ att, folder, onRemove }: { att: Attachment; folder: string; onRemove: () => void }) {
  const [size, setSize] = useState('');
  return (
    <span className="att" title={att.path ?? att.file.name}>
      {att.image ? (
        <img
          src={att.url!}
          alt=""
          onLoad={(e) => setSize(`${e.currentTarget.naturalWidth}×${e.currentTarget.naturalHeight}`)}
        />
      ) : (
        <span className="ficon">
          <Icon name="file-earmark-text" />
        </span>
      )}
      <span className="meta">
        <span className="ell">{att.file.name}</span>
        <small className="ell">{att.image ? t('attach.image', { size }) : where(att.path!, folder)}</small>
      </span>
      <button className="icon-btn" type="button" title={t('attach.remove')} onClick={onRemove}>
        <Icon name="x-lg" />
      </button>
    </span>
  );
}

function Composer({
  room,
  toolName,
  onTerminal,
  atts,
  onFiles,
  onPick,
  onRemove,
  onSent,
}: { room: Room; toolName: string; onTerminal: () => void } & AttachProps) {
  const send = useSendToRoom();
  const [attaching, setAttaching] = useState(false);
  const [attachFailed, setAttachFailed] = useState(false);
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

  // 그림을 먼저 하나씩 첨부하고(CLI 입력 줄에 [Image #1] 처럼 들어간다) 글과 파일 경로를 붙여 Enter
  const submit = async (text: string) => {
    if (exited || attaching) return;
    const paths = atts.filter((a) => !a.image).map((a) => a.path!);
    const images = atts.filter((a) => a.image).map((a) => a.file);
    const full = (text.trim() + (paths.length ? quotePaths(paths) : '')).trim();
    if (!full) return;
    setAttachFailed(false);
    if (images.length) {
      setAttaching(true);
      const done = await attachImages(room.id, images);
      setAttaching(false);
      if (done < images.length) {
        setAttachFailed(true);
        return;
      }
    }
    send.mutate(
      { id: room.id, text: full },
      {
        onSuccess: () => {
          setDraft('');
          onSent();
        },
      },
    );
  };

  const choose = (name: string, terminal: boolean) => {
    if (terminal) {
      // 화면이 있어야 하는 명령은 보내고 터미널 보기로 넘어간다. 첨부는 남겨 둔다
      send.mutate({ id: room.id, text: name }, { onSuccess: () => setDraft('') });
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
      void submit(draft);
    }
  };
  const ready = (draft.trim() || atts.some((a) => !a.image)) && !send.isPending && !attaching && !exited;

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
        {atts.length > 0 && (
          <div className="atts">
            {atts.map((att) => (
              <Chip key={att.key} att={att} folder={room.folder} onRemove={() => onRemove(att.key)} />
            ))}
          </div>
        )}
        <button className="icon-btn" type="button" title={t('attach.pick')} disabled={exited} onClick={onPick}>
          <Icon name="paperclip" />
        </button>
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
          onPaste={(e) => {
            const files = filesOf(e.clipboardData);
            if (!files.length) return;
            e.preventDefault();
            onFiles(files);
          }}
        />
        <button
          className="send"
          type="button"
          title={t('chat.send')}
          disabled={!ready}
          onClick={() => void submit(draft)}
        >
          <Icon name="send-fill" />
        </button>
      </div>
      <div className="compose-hint">
        <span>{t('chat.hintKeys')}</span>
        <span className={send.isError || attachFailed ? 'bad' : undefined}>
          {attachFailed
            ? t('attach.failed')
            : send.isError
              ? t('chat.sendFailed')
              : attaching
                ? t('attach.sending')
                : atts.length
                  ? t('attach.hint', { tool: toolName })
                  : t('chat.hintWhere', { tool: toolName })}
        </span>
      </div>
    </div>
  );
}

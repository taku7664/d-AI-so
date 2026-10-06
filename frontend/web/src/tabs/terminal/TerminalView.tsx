// 터미널 탭 (docs/design/daiso-d.html "터미널"). 위에 방 줄, 아래 고른 방의 화면. "+" 는 새 터미널 카드
// 지금 프로젝트의 방만 보인다. 다른 프로젝트 방은 위 줄 종에서 본다
import { useEffect, useRef, useState } from 'react';
import { Icon } from '../../icons/Icon';
import { useCurrentProject } from '../../project';
import { t } from '../../strings';
import { ToolBadge, useTools } from '../../tools';
import {
  NEW_TERMINAL_EVENT,
  ROOM_EVENT,
  clearPendingFolder,
  clearPendingRoom,
  inProject,
  markSeen,
  openFolder,
  peekPendingFolder,
  peekPendingRoom,
  useCloseRoom,
  useRenameRoom,
  useRooms,
  type Room,
} from './api';
import { ChatView } from './ChatView';
import { EMPTY_DRAFT, NewTerminal, type Draft } from './NewTerminal';
import { TermScreen, type TermHandle } from './TermScreen';

export const STATE_ICON: Record<string, string> = {
  run: 'arrow-repeat',
  done: 'check2-circle',
  ask: 'hourglass-split',
  idle: 'keyboard',
  exited: 'x-circle-fill',
};

export function TerminalView() {
  const rooms = useRooms();
  const tools = useTools();
  const project = useCurrentProject();
  const close = useCloseRoom();
  const [active, setActive] = useState<string | null>(() => peekPendingRoom() ?? (peekPendingFolder() ? 'new' : null));
  const [draft, setDraft] = useState<Draft>(() => ({ ...EMPTY_DRAFT, folder: peekPendingFolder() }));
  useEffect(() => {
    clearPendingRoom();
    clearPendingFolder();
    const onRoom = (event: Event) => {
      setActive((event as CustomEvent<string>).detail);
      clearPendingRoom();
    };
    const onNew = (event: Event) => {
      setDraft({ ...EMPTY_DRAFT, folder: (event as CustomEvent<string>).detail });
      setActive('new');
      clearPendingFolder();
    };
    window.addEventListener(ROOM_EVENT, onRoom);
    window.addEventListener(NEW_TERMINAL_EVENT, onNew);
    return () => {
      window.removeEventListener(ROOM_EVENT, onRoom);
      window.removeEventListener(NEW_TERMINAL_EVENT, onNew);
    };
  }, []);

  const visible = (rooms.data ?? []).filter((room) => inProject(room, project?.members ?? null));
  const others = (rooms.data?.length ?? 0) - visible.length;
  const room = visible.find((r) => r.id === active) ?? (active === 'new' ? null : (visible[0] ?? null));
  const showNew = active === 'new' || !room;

  // 보고 있는 방의 "안 본 답"을 지운다. 창이 앞에 있을 때만 본 것으로 친다
  useEffect(() => {
    if (!room?.unseen) return;
    const seen = () => document.hasFocus() && markSeen(room.id);
    seen();
    window.addEventListener('focus', seen);
    return () => window.removeEventListener('focus', seen);
  }, [room?.id, room?.unseen]);

  const toolOf = (id: string) => tools.data?.find((tool) => tool.id === id);

  return (
    <>
      <div className="rooms" role="tablist">
        <button
          className="room-plus"
          type="button"
          aria-pressed={showNew}
          title={t('terminal.newHint')}
          onClick={() => setActive('new')}
        >
          <Icon name="plus-lg" />
        </button>
        {visible.map((r) => {
          const tool = toolOf(r.tool);
          return (
            <span
              key={r.id}
              className="room"
              role="tab"
              aria-pressed={!showNew && r.id === room?.id}
              title={`${r.name} · ${t(`terminal.state.${r.state}` as 'terminal.state.run')}`}
            >
              <button type="button" className="room-main" onClick={() => setActive(r.id)}>
                {tool && <ToolBadge tool={tool} small />}
                <span className="name">{project ? r.name : `${r.folder.split(/[\\/]/).pop()} · ${r.name}`}</span>
                {r.state === 'ask' && (
                  <span className="st-ask">
                    <Icon name="hourglass-split" />
                  </span>
                )}
                {r.unseen && <span className="new-dot" title={t('terminal.unseen')} />}
              </button>
              <button
                className="x"
                type="button"
                title={t('terminal.closeHint')}
                onClick={() => {
                  close.mutate(r.id);
                  if (r.id === room?.id) setActive(null);
                }}
              >
                <Icon name="x-lg" />
              </button>
            </span>
          );
        })}
        {others > 0 && <span className="others">{t('terminal.otherRooms', { count: others })}</span>}
      </div>
      {showNew ? (
        <NewTerminal
          draft={draft}
          onDraft={setDraft}
          onOpened={(opened) => {
            setDraft(EMPTY_DRAFT);
            setActive(opened.id);
          }}
        />
      ) : (
        <RoomBody
          key={room!.id}
          room={room!}
          onSameFolder={() => {
            setDraft({ ...EMPTY_DRAFT, tool: room!.tool, folder: room!.folder });
            setActive('new');
          }}
        />
      )}
    </>
  );
}

// 방마다 마지막으로 고른 보기. 말풍선은 세션 기록을 아는 도구(Claude·Codex)만
const VIEWS = new Map<string, 'chat' | 'term'>();
const CHAT_TOOLS = new Set(['claude', 'codex']);

function RoomBody({ room, onSameFolder }: { room: Room; onSameFolder: () => void }) {
  const screen = useRef<TermHandle>(null);
  const canChat = CHAT_TOOLS.has(room.tool);
  const [view, setViewState] = useState<'chat' | 'term'>(VIEWS.get(room.id) ?? (canChat ? 'chat' : 'term'));
  const setView = (next: 'chat' | 'term') => {
    VIEWS.set(room.id, next);
    setViewState(next);
    if (next === 'term') window.setTimeout(() => screen.current?.focus(), 0);
  };
  const rename = useRenameRoom();
  const [finding, setFinding] = useState(false);
  const [query, setQuery] = useState('');
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(room.name);
  const [more, setMore] = useState(false);
  const [note, setNote] = useState<string | null>(null);

  const flash = (text: string) => {
    setNote(text);
    window.setTimeout(() => setNote(null), 2000);
  };

  const saveScreen = () => {
    const blob = new Blob([screen.current?.text() ?? ''], { type: 'text/plain;charset=utf-8' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `${room.folder.split(/[\\/]/).pop()}-${new Date().toISOString().slice(0, 16).replace(/[-:T]/g, '')}.txt`;
    link.click();
    URL.revokeObjectURL(link.href);
  };

  return (
    <div className="roombody">
      <div className="rtools">
        {finding ? (
          <div className="findbar">
            <Icon name="search" />
            <input
              className="input"
              autoFocus
              placeholder={t('terminal.find')}
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                screen.current?.find(e.target.value);
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') screen.current?.find(query, e.shiftKey);
                if (e.key === 'Escape') {
                  setFinding(false);
                  screen.current?.clearFind();
                  screen.current?.focus();
                }
              }}
            />
            <button
              className="icon-btn"
              type="button"
              title={t('terminal.findPrev')}
              onClick={() => screen.current?.find(query, true)}
            >
              <Icon name="arrow-up" />
            </button>
            <button
              className="icon-btn"
              type="button"
              title={t('terminal.findNext')}
              onClick={() => screen.current?.find(query)}
            >
              <Icon name="arrow-down" />
            </button>
            <button
              className="icon-btn"
              type="button"
              title={t('terminal.findClose')}
              onClick={() => {
                setFinding(false);
                screen.current?.clearFind();
              }}
            >
              <Icon name="x-lg" />
            </button>
          </div>
        ) : (
          <>
            {canChat && (
              <span className="seg view-seg">
                <button
                  type="button"
                  aria-pressed={view === 'chat'}
                  title={t('chat.viewHint')}
                  onClick={() => setView('chat')}
                >
                  <Icon name="chat-dots" />
                  {t('chat.view')}
                </button>
                <button type="button" aria-pressed={view === 'term'} onClick={() => setView('term')}>
                  <Icon name="terminal" />
                  {t('tab.terminal')}
                </button>
              </span>
            )}
            {view === 'term' && (
              <button
                className="icon-btn"
                type="button"
                title={t('terminal.findHint')}
                onClick={() => setFinding(true)}
              >
                <Icon name="search" />
              </button>
            )}
            <span className="sep" />
            <button
              className="icon-btn"
              type="button"
              title={t('terminal.openFolder')}
              onClick={() => openFolder(room.folder)}
            >
              <Icon name="folder2-open" />
            </button>
            <span className="anchor">
              <button className="icon-btn" type="button" title={t('terminal.more')} onClick={() => setMore(!more)}>
                <Icon name="three-dots" />
              </button>
              {more && (
                <div className="pop" style={{ width: 230 }} onClick={() => setMore(false)}>
                  <button className="menu-item" type="button" onClick={() => screen.current?.send('\x05\x15')}>
                    <Icon name="eraser" />
                    {t('terminal.clearInput')}
                  </button>
                  <button className="menu-item" type="button" onClick={onSameFolder}>
                    <Icon name="window-plus" />
                    {t('terminal.sameFolder')}
                  </button>
                  <div className="menu-sep" />
                  <button
                    className="menu-item"
                    type="button"
                    onClick={() =>
                      void navigator.clipboard
                        .writeText(screen.current?.text() ?? '')
                        .then(() => flash(t('terminal.copied')))
                    }
                  >
                    <Icon name="clipboard" />
                    {t('terminal.copyScreen')}
                  </button>
                  <button className="menu-item" type="button" onClick={saveScreen}>
                    <Icon name="download" />
                    {t('terminal.saveScreen')}
                  </button>
                </div>
              )}
            </span>
            {note && <span className="lnote">{note}</span>}
            <span className="title ell">
              {editing ? (
                <input
                  className="input title-edit"
                  autoFocus
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  onBlur={() => {
                    setEditing(false);
                    if (name.trim() && name !== room.name) rename.mutate({ id: room.id, name });
                  }}
                  onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
                />
              ) : (
                <>
                  <span
                    className={`pill ${room.state === 'done' ? 'ok' : room.state === 'ask' ? 'warn' : room.state === 'exited' ? 'bad' : 'plain'}`}
                  >
                    <Icon name={STATE_ICON[room.state] ?? 'keyboard'} />
                    {t(`terminal.state.${room.state}` as 'terminal.state.run')}
                  </span>
                  <span title={room.folder}>{room.name}</span>
                  <button
                    className="icon-btn"
                    type="button"
                    title={t('terminal.rename')}
                    onClick={() => setEditing(true)}
                  >
                    <Icon name="pencil" />
                  </button>
                </>
              )}
            </span>
          </>
        )}
      </div>
      {/* 터미널은 말풍선을 보는 동안에도 붙어 있는다. 다시 터미널로 오면 CLI 화면이 그대로다 */}
      {view === 'chat' && <ChatView room={room} onTerminal={() => setView('term')} />}
      <div className="term-wrap" hidden={view === 'chat'}>
        <TermScreen ref={screen} roomId={room.id} onFind={() => setFinding(true)} onExit={() => undefined} />
      </div>
    </div>
  );
}

// 위 줄 종: 모든 프로젝트의 터미널 방 상태. 사람이 봐야 하는 방(허락 기다림, 안 본 답) 수를 붙인다
import { useEffect, useRef, useState } from 'react';
import { Icon } from '../icons/Icon';
import { navigate } from '../router';
import { t } from '../strings';
import { useCurrentProject, useProjects, useSetCurrentProject } from '../project';
import { inProject, requestRoom, useRooms } from '../tabs/terminal/api';
import { STATE_ICON } from '../tabs/terminal/TerminalView';
import { ToolBadge, useTools } from '../tools';

const PILL: Record<string, string> = { done: 'ok', ask: 'warn', exited: 'bad' };

export function Bell() {
  const rooms = useRooms();
  const current = useCurrentProject();
  const projects = useProjects();
  const setProject = useSetCurrentProject();
  const tools = useTools();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLSpanElement>(null);
  const list = rooms.data ?? [];
  const need = list.filter((room) => room.state === 'ask' || room.unseen).length;

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [open]);

  const go = (id?: string) => {
    const room = list.find((r) => r.id === id);
    // 지금 프로젝트 밖의 방이면 그 방의 프로젝트로 바꾼다. 터미널 탭은 지금 프로젝트의 방만 보인다
    if (room && !inProject(room, current?.members ?? null)) {
      const owner = projects.data?.projects.find((p) => inProject(room, p.members));
      setProject.mutate(owner?.path ?? null);
    }
    if (id) requestRoom(id);
    setOpen(false);
    navigate('terminal');
  };

  return (
    <span className="anchor bell" ref={box}>
      <button
        className="icon-btn"
        type="button"
        aria-pressed={open}
        title={t('top.bell')}
        onClick={() => setOpen(!open)}
      >
        <Icon name={need ? 'bell-fill' : 'bell'} label={t('top.bell')} />
      </button>
      {need > 0 && <span className="badge">{need}</span>}
      {open && (
        <div className="pop right bell-pop">
          <div className="pop-head">
            {t('bell.rooms', { count: list.length })}
            <button className="link" type="button" onClick={() => go()}>
              {t('tab.terminal')}
            </button>
          </div>
          {!list.length && <div className="pop-note">{t('bell.none')}</div>}
          {list.map((room) => {
            const tool = tools.data?.find((x) => x.id === room.tool);
            return (
              <button key={room.id} className="troom" type="button" onClick={() => go(room.id)}>
                {tool ? <ToolBadge tool={tool} /> : <span />}
                <span className="ell">
                  <b>{room.name}</b> {room.unseen && <span className="new-dot" />}
                </span>
                <small className="ell">{room.folder.split(/[\\/]/).pop()}</small>
                <span className={`pill ${PILL[room.state] ?? 'plain'}`}>
                  <Icon name={STATE_ICON[room.state] ?? 'keyboard'} />
                  {t(`terminal.state.${room.state}` as 'terminal.state.run')}
                </span>
              </button>
            );
          })}
        </div>
      )}
    </span>
  );
}

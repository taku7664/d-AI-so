// 위 줄 종 (docs/design/summary.html 종 팝업). 어느 프로젝트를 골랐든 모든 프로젝트 기준이다.
// 위에서부터 손볼 것, 열린 터미널, 마지막으로 하던 것, 다른 프로젝트에서 하던 것. 숫자는 사람이 봐야 하는 방(허락 기다림, 안 본 답)만 센다
import { useMutation, useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useLogin } from '../accounts';
import { api, type Schemas } from '../api/client';
import { bytes, dateTime, when } from '../format';
import { Icon } from '../icons/Icon';
import { navigate } from '../router';
import { t } from '../strings';
import { useCurrentProject, useSetCurrentProject } from '../project';
import { requestOpen } from '../tabs/sessions/api';
import { useResumeInRoom, useRooms } from '../tabs/terminal/api';
import { useGoToRoom } from './goToRoom';
import { STATE_ICON } from '../tabs/terminal/TerminalView';
import { ToolBadge, useTools, type Tool } from '../tools';

const PILL: Record<string, string> = { done: 'ok', ask: 'warn', exited: 'bad' };

type Work = Schemas['ProjectWork'];

function useBell(enabled: boolean) {
  return useQuery({
    // 인덱스·계정·세션이 바뀌면 다시 받는다 (api/events.ts)
    queryKey: ['bell'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/bell');
      if (error || !data) throw new Error('알림을 읽지 못했다');
      return data;
    },
    enabled,
  });
}

export function Bell() {
  const rooms = useRooms();
  const tools = useTools();
  const [open, setOpen] = useState(false);
  const bell = useBell(open);
  const box = useRef<HTMLSpanElement>(null);
  const list = rooms.data ?? [];
  const need = list.filter((room) => room.state === 'ask' || room.unseen).length;
  const toolOf = (id: string | null | undefined) => tools.data?.find((tool) => tool.id === id);

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [open]);

  const goToRoom = useGoToRoom();
  const go = (id?: string) => {
    setOpen(false);
    if (id) goToRoom(id);
    else navigate('terminal');
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
          {bell.data && bell.data.attention.length > 0 && (
            <>
              <div className="pop-head">
                {t('bell.attention')} <span className="num">{bell.data.attention.length}</span>
              </div>
              {bell.data.attention.map((item, i) => (
                <AttentionRow key={i} item={item} toolOf={toolOf} onDone={() => setOpen(false)} />
              ))}
              <div className="menu-sep" />
            </>
          )}
          <div className="pop-head">
            {t('bell.rooms', { count: list.length })}
            <button className="link" type="button" onClick={() => go()}>
              {t('tab.terminal')}
            </button>
          </div>
          {!list.length && <div className="pop-note">{t('bell.none')}</div>}
          {list.map((room) => {
            const tool = toolOf(room.tool);
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
          {bell.data?.last && <LastWork work={bell.data.last} toolOf={toolOf} onDone={() => setOpen(false)} />}
          {bell.data && bell.data.others.length > 0 && (
            <>
              <div className="pop-head">
                {t('bell.others')}
                <span className="hint">{t('bell.othersHint')}</span>
              </div>
              {bell.data.others.map((work) => (
                <OtherWork key={work.work.session.path} work={work} toolOf={toolOf} onDone={() => setOpen(false)} />
              ))}
            </>
          )}
        </div>
      )}
    </span>
  );
}

/** 이 세션의 프로젝트로 바꾼 뒤 이어서 연다. 터미널·세션 탭은 지금 프로젝트 것만 보인다 */
function useOpenWork(onDone: () => void) {
  const current = useCurrentProject();
  const setProject = useSetCurrentProject();
  const resume = useResumeInRoom();
  const inPlace = (work: Work, then: () => void) => {
    if (current?.path?.toLowerCase() === work.projectPath.toLowerCase()) then();
    else setProject.mutate(work.projectPath, { onSuccess: then });
  };
  return {
    pending: resume.isPending || setProject.isPending,
    resume: (work: Work) =>
      inPlace(work, () =>
        resume.mutate(work.work.session, {
          onSuccess: (id) => {
            onDone();
            if (id) navigate('terminal');
          },
        }),
      ),
    view: (work: Work) =>
      inPlace(work, () => {
        requestOpen(work.work.session.path);
        onDone();
        navigate('sessions');
      }),
  };
}

function LastWork({
  work,
  toolOf,
  onDone,
}: {
  work: Work;
  toolOf: (id: string) => Tool | undefined;
  onDone: () => void;
}) {
  const open = useOpenWork(onDone);
  const session = work.work.session;
  const tool = toolOf(session.tool);
  return (
    <div className="last-work">
      <span className="k">{t('bell.last', { at: when(session.modifiedAt) })}</span>
      <b className="q">{work.work.lastPrompt ?? (session.title || t('sessions.noPrompt'))}</b>
      <span className="meta">
        {tool && <ToolBadge tool={tool} small />}
        <span className="ptag">{work.projectLabel}</span>
        <span className="num">
          {t('dashboard.turns', { count: session.userMessages })} · {bytes(session.sizeBytes)}
        </span>
      </span>
      <span className="acts">
        <button className="btn small primary" type="button" disabled={open.pending} onClick={() => open.resume(work)}>
          <Icon name="arrow-repeat" />
          {t('bell.resume')}
        </button>
        <button className="btn small" type="button" disabled={open.pending} onClick={() => open.view(work)}>
          <Icon name="eye" />
          {t('dashboard.view')}
        </button>
      </span>
    </div>
  );
}

function OtherWork({
  work,
  toolOf,
  onDone,
}: {
  work: Work;
  toolOf: (id: string) => Tool | undefined;
  onDone: () => void;
}) {
  const open = useOpenWork(onDone);
  const session = work.work.session;
  const tool = toolOf(session.tool);
  const text = work.work.lastPrompt ?? (session.title || t('sessions.noPrompt'));
  return (
    <div className="orow">
      {tool ? <ToolBadge tool={tool} small /> : <span />}
      <span className="ptag">{work.projectLabel}</span>
      <span className="ell" title={text}>
        {text}
        <small className="num">
          {when(session.modifiedAt)} · {t('dashboard.turns', { count: session.userMessages })}
        </small>
      </span>
      <button
        className="icon-btn"
        type="button"
        title={t('dashboard.resume')}
        disabled={open.pending}
        onClick={() => open.resume(work)}
      >
        <Icon name="arrow-repeat" />
      </button>
    </div>
  );
}

function AttentionRow({
  item,
  toolOf,
  onDone,
}: {
  item: Schemas['Attention'];
  toolOf: (id: string) => Tool | undefined;
  onDone: () => void;
}) {
  const login = useLogin();
  const rebuild = useMutation({
    mutationFn: async () => {
      await api.POST('/api/index/rebuild');
    },
  });
  const toolName = toolOf(item.tool ?? '')?.title ?? item.tool ?? '';
  const icon =
    item.kind === 'account'
      ? 'clock'
      : item.kind === 'cleanup'
        ? 'hdd'
        : item.kind === 'plugin'
          ? 'plug'
          : 'exclamation-triangle-fill';

  return (
    <div className={`attn ${item.level}`}>
      <Icon name={icon} />
      {item.kind === 'account' && (
        <>
          <span>
            <b>
              {t(
                item.state === 'expiringSoon'
                  ? 'dashboard.expiringSoon'
                  : item.state === 'expired'
                    ? 'dashboard.expired'
                    : 'dashboard.noLogin',
                { tool: toolName },
              )}
            </b>
            {item.at && <small>{t('account.until', { at: dateTime(item.at) })}</small>}
          </span>
          <button
            className="btn small"
            type="button"
            disabled={login.isPending}
            onClick={() => login.mutate(item.tool!)}
          >
            <Icon name="box-arrow-in-right" />
            {item.state === 'missing' ? t('account.login') : t('account.relogin')}
          </button>
        </>
      )}
      {item.kind === 'cleanup' && (
        <>
          <span>
            <b>{t('dashboard.cleanup', { count: item.count ?? 0, size: bytes(item.bytes ?? 0) })}</b>
            <small>{t('dashboard.cleanupHint')}</small>
          </span>
          <button
            className="btn small"
            type="button"
            onClick={() => {
              onDone();
              navigate('sessions');
            }}
          >
            <Icon name="check2-square" />
            {t('dashboard.goPick')}
          </button>
        </>
      )}
      {item.kind === 'index' && (
        <>
          <span>
            <b>{t('dashboard.indexBroken')}</b>
            <small>{item.detail}</small>
          </span>
          <button className="btn small" type="button" disabled={rebuild.isPending} onClick={() => rebuild.mutate()}>
            <Icon name="arrow-repeat" />
            {t('dashboard.rebuild')}
          </button>
        </>
      )}
      {item.kind === 'plugin' && (
        <>
          <span>
            <b>{t('dashboard.plugin', { name: item.name ?? '' })}</b>
            <small>{item.detail}</small>
          </span>
          <button
            className="btn small"
            type="button"
            onClick={() => {
              onDone();
              navigate('settings');
            }}
          >
            <Icon name="gear" />
            {t('tab.settings')}
          </button>
        </>
      )}
    </div>
  );
}

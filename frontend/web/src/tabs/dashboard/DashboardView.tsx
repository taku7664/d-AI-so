// 요약 탭 (docs/design/daiso-d.html "요약"). 프로젝트를 골랐으면 그 프로젝트의 첫 화면, 아니면 프로젝트 카드
import { useMutation, useQuery } from '@tanstack/react-query';
import { useLogin } from '../../accounts';
import { api, type Schemas } from '../../api/client';
import { bytes, dateTime, tokens } from '../../format';
import { Icon } from '../../icons/Icon';
import { useCurrentProject, useSetCurrentProject } from '../../project';
import { navigate } from '../../router';
import { t } from '../../strings';
import { ToolBadge, useTools, type Tool } from '../../tools';
import { requestOpen } from '../sessions/api';
import { requestRoom, useResumeInRoom, useRooms, inProject } from '../terminal/api';

type Dashboard = Schemas['DashboardResponse'];

function useDashboard(project: string | null) {
  return useQuery({
    // 계정·인덱스·세션이 바뀌면 서버가 dashboard 알림을 보낸다
    queryKey: ['dashboard', project],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/dashboard', { params: { query: { project: project ?? undefined } } });
      if (error || !data) throw new Error('요약을 읽지 못했다');
      return data;
    },
  });
}

export function DashboardView() {
  const project = useCurrentProject();
  const dashboard = useDashboard(project?.path ?? null);
  const tools = useTools();
  const toolOf = (id: string | null | undefined) => tools.data?.find((tool) => tool.id === id);

  if (dashboard.isError) return <div className="empty">{t('dashboard.failed')}</div>;
  if (!dashboard.data) return <div className="centered">{t('top.project.loading')}</div>;

  return project ? (
    <ProjectHome data={dashboard.data} name={project.name} toolOf={toolOf} />
  ) : (
    <AllProjects data={dashboard.data} toolOf={toolOf} />
  );
}

function ProjectHome({
  data,
  name,
  toolOf,
}: {
  data: Dashboard;
  name: string;
  toolOf: (id: string) => Tool | undefined;
}) {
  const resume = useResumeInRoom();
  const last = data.recent[0]?.modifiedAt;
  return (
    <>
      <div className="phead">
        <h2>{name}</h2>
        {last && <span className="state">{t('dashboard.lastWork', { at: dateTime(last) })}</span>}
      </div>
      <div className="home">
        <div className="col">
          <section className="card box">
            <div className="box-head">
              <h3>{t('dashboard.openTerminals')}</h3>
            </div>
            <OpenRooms />
          </section>
          <section className="card box">
            <div className="box-head">
              <h3>{t('dashboard.recent')}</h3>
              <span className="r">
                <button className="link" type="button" onClick={() => navigate('sessions')}>
                  {t('dashboard.seeAll')}
                </button>
              </span>
            </div>
            {!data.recent.length && <div className="faint">{t('dashboard.noSessions')}</div>}
            {data.recent.map((session) => {
              const tool = toolOf(session.tool);
              return (
                <div className="srow2" key={session.path}>
                  {tool ? <ToolBadge tool={tool} /> : <span />}
                  <span className="t ell" title={session.title}>
                    {session.title || t('sessions.noPrompt')}
                  </span>
                  <small className="num">
                    {dateTime(session.modifiedAt)} · {bytes(session.sizeBytes)} ·{' '}
                    {t('dashboard.turns', { count: session.userMessages })}
                  </small>
                  <span className="acts">
                    <button
                      className="btn small"
                      type="button"
                      title={t('sessions.resumeHint')}
                      disabled={resume.isPending}
                      onClick={() => resume.mutate(session, { onSuccess: (id) => id && navigate('terminal') })}
                    >
                      <Icon name="arrow-repeat" />
                      {t('dashboard.resume')}
                    </button>
                    <button
                      className="icon-btn"
                      type="button"
                      title={t('dashboard.view')}
                      onClick={() => {
                        requestOpen(session.path);
                        navigate('sessions');
                      }}
                    >
                      <Icon name="eye" />
                    </button>
                  </span>
                </div>
              );
            })}
          </section>
        </div>
        <div className="col">
          <AttentionBox items={data.attention} toolOf={toolOf} />
          <section className="card box">
            <div className="box-head">
              <h3>{t('dashboard.thisProject')}</h3>
              <span className="r">
                <button className="link" type="button" onClick={() => navigate('usage')}>
                  {t('tab.usage')}
                </button>
              </span>
            </div>
            <Stats stats={data.stats} />
          </section>
        </div>
      </div>
    </>
  );
}

function AllProjects({ data, toolOf }: { data: Dashboard; toolOf: (id: string) => Tool | undefined }) {
  const setProject = useSetCurrentProject();
  return (
    <>
      <div className="phead">
        <h2>{t('top.project.all')}</h2>
        <span className="state">{t('dashboard.projectCount', { count: data.projects.length })}</span>
      </div>
      {/* 급한 것이 먼저다. 프로젝트가 많으면 카드 아래 둔 손볼 것이 화면 밖으로 밀린다 */}
      <div className="two">
        <AttentionBox items={data.attention} toolOf={toolOf} />
        <section className="card box">
          <div className="box-head">
            <h3>{t('dashboard.everything')}</h3>
          </div>
          <Stats stats={data.stats} />
        </section>
      </div>
      <div className="pgrid">
        {data.projects.map((card) => (
          <button key={card.path} className="pcard" type="button" onClick={() => setProject.mutate(card.path)}>
            <span className="top">
              <Icon name="folder" />
              <b className="ell">{card.label}</b>
              {!card.exists && <span className="pill bad">{t('sessions.folderMissing')}</span>}
            </span>
            <span className="path ell" title={card.path}>
              {card.path}
            </span>
            <span className="foot2 num">
              {card.lastActivity && (
                <span>
                  <Icon name="clock" /> {dateTime(card.lastActivity)}
                </span>
              )}
              <span>{t('dashboard.sessions', { count: card.sessions })}</span>
              <span>{bytes(card.sizeBytes)}</span>
            </span>
          </button>
        ))}
      </div>
    </>
  );
}

function Stats({ stats }: { stats: Schemas['DashboardStats'] }) {
  return (
    <div className="stats3 num">
      <div>
        <span>{t('tab.sessions')}</span>
        <b>{stats.sessions}</b>
      </div>
      <div>
        <span>{t('dashboard.size')}</span>
        <b>{bytes(stats.sizeBytes)}</b>
      </div>
      <div title={stats.last7Days.total.toLocaleString('ko-KR')}>
        <span>{t('dashboard.week')}</span>
        <b>{tokens(stats.last7Days.total)}</b>
      </div>
    </div>
  );
}

function AttentionBox({ items, toolOf }: { items: Schemas['Attention'][]; toolOf: (id: string) => Tool | undefined }) {
  const login = useLogin();
  const rebuild = useMutation({
    mutationFn: async () => {
      await api.POST('/api/index/rebuild');
    },
  });

  return (
    <section className="card box">
      <div className="box-head">
        <h3>{t('dashboard.attention')}</h3>
        {items.length > 0 && <span className="faint num">{items.length}</span>}
      </div>
      {!items.length && (
        <div className="allgood">
          <Icon name="check-circle-fill" />
          {t('dashboard.allGood')}
        </div>
      )}
      {items.map((item, i) => {
        const tool = toolOf(item.tool ?? '');
        const toolName = tool?.title ?? item.tool ?? '';
        return (
          <div key={i} className={`attn ${item.level}`}>
            <Icon
              name={
                item.kind === 'account'
                  ? 'clock'
                  : item.kind === 'cleanup'
                    ? 'hdd'
                    : item.kind === 'plugin'
                      ? 'plug'
                      : 'exclamation-triangle-fill'
              }
            />
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
                <button className="btn small" type="button" onClick={() => navigate('sessions')}>
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
                <button
                  className="btn small"
                  type="button"
                  disabled={rebuild.isPending}
                  onClick={() => rebuild.mutate()}
                >
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
                <button className="btn small" type="button" onClick={() => navigate('settings')}>
                  <Icon name="gear" />
                  {t('tab.settings')}
                </button>
              </>
            )}
          </div>
        );
      })}
    </section>
  );
}

/** 이 프로젝트의 열린 터미널 방. 누르면 그 방으로 간다 */
function OpenRooms() {
  const rooms = useRooms();
  const project = useCurrentProject();
  const tools = useTools();
  const list = (rooms.data ?? []).filter((room) => inProject(room, project?.members ?? null));
  if (!list.length)
    return (
      <div className="empty">
        {t('dashboard.noRooms')}{' '}
        <button className="btn small" type="button" onClick={() => navigate('terminal')}>
          <Icon name="plus-lg" />
          {t('dashboard.newTerminal')}
        </button>
      </div>
    );
  return (
    <>
      {list.map((room) => {
        const tool = tools.data?.find((x) => x.id === room.tool);
        return (
          <div className="trow" key={room.id}>
            {tool ? <ToolBadge tool={tool} /> : <span />}
            <span className="ell">
              <b>{room.name}</b> {room.unseen && <span className="pill ok">{t('terminal.unseen')}</span>}
            </span>
            <span
              className={`pill ${room.state === 'done' ? 'ok' : room.state === 'ask' ? 'warn' : room.state === 'exited' ? 'bad' : 'plain'}`}
            >
              {t(`terminal.state.${room.state}` as 'terminal.state.run')}
            </span>
            <button
              className="btn small"
              type="button"
              onClick={() => {
                requestRoom(room.id);
                navigate('terminal');
              }}
            >
              <Icon name="terminal" />
              {t('dashboard.openRoom')}
            </button>
          </div>
        );
      })}
    </>
  );
}

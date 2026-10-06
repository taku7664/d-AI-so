// 요약 탭 (docs/design/summary.html, docs/DECISIONS.md "요약은 지금 상황"). 위에 타일 넷, 아래에 최근 세션과 워크트리.
// 프로젝트를 고르면 같은 틀을 그 프로젝트로 좁힌다. 손볼 것과 마지막으로 하던 것은 위 줄 종 팝업(layout/Bell.tsx)에 있다
import { useQuery } from '@tanstack/react-query';
import { api, type Schemas } from '../../api/client';
import { bytes, dateTime, exact, tokens, when } from '../../format';
import { Icon } from '../../icons/Icon';
import { useCurrentProject } from '../../project';
import { navigate } from '../../router';
import { has, t } from '../../strings';
import { ToolBadge, useTools, type Tool } from '../../tools';
import { requestOpen } from '../sessions/api';
import { inProject, requestNewTerminal, useResumeInRoom, useRooms } from '../terminal/api';
import { useLimits } from '../usage/LimitsBox';

type Dashboard = Schemas['DashboardResponse'];
type Worktree = Schemas['WorktreeItem'];

function useDashboard(project: string | null) {
  return useQuery({
    // 인덱스·세션이 바뀌면 서버가 dashboard 알림을 보낸다. 세션 폴더를 지켜보므로 다른 터미널에서 친 채팅도 따라온다
    queryKey: ['dashboard', project],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/dashboard', { params: { query: { project: project ?? undefined } } });
      if (error || !data) throw new Error('요약을 읽지 못했다');
      return data;
    },
  });
}

function useWorktrees(project: string | null) {
  return useQuery({
    // git 을 여러 번 부르므로 타일과 따로 받는다. 늦게 와도 위쪽은 먼저 보인다
    queryKey: ['dashboard', 'worktrees', project],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/dashboard/worktrees', {
        params: { query: { project: project ?? undefined } },
      });
      if (error || !data) throw new Error('워크트리를 읽지 못했다');
      return data;
    },
  });
}

export function DashboardView() {
  const project = useCurrentProject();
  const scope = project?.path ?? null;
  const dashboard = useDashboard(scope);
  const tools = useTools();
  const toolOf = (id: string | null | undefined) => tools.data?.find((tool) => tool.id === id);

  if (dashboard.isError) return <div className="empty">{t('dashboard.failed')}</div>;
  if (!dashboard.data) return <div className="centered">{t('top.project.loading')}</div>;

  return (
    <>
      <Tiles data={dashboard.data} members={project?.members ?? null} />
      <div className="sum-lower">
        <RecentBox data={dashboard.data} toolOf={toolOf} />
        <WorktreeBox project={scope} />
      </div>
    </>
  );
}

// ── 타일 ──

function Tiles({ data, members }: { data: Dashboard; members: readonly string[] | null }) {
  const rooms = (useRooms().data ?? []).filter((room) => inProject(room, members));
  const need = rooms.filter((room) => room.state === 'ask' || room.unseen).length;
  const week = data.week.reduce((sum, day) => sum + day.tokens, 0);
  const max = Math.max(1, ...data.week.map((day) => day.tokens));

  return (
    <div className="tiles">
      <button className="tile card" type="button" onClick={() => navigate('terminal')}>
        <span className="k">
          <Icon name="terminal" />
          {t('dashboard.tile.rooms')}
        </span>
        <span className="v num">{rooms.length}</span>
        <span className="s">
          {!rooms.length
            ? t('dashboard.tile.roomsNone')
            : need
              ? t('dashboard.tile.roomsNeed', { count: need })
              : t('dashboard.tile.roomsFine')}
        </span>
      </button>
      <button className="tile card" type="button" onClick={() => navigate('sessions')}>
        <span className="k">
          <Icon name="chat-left-text" />
          {t('dashboard.tile.today')}
        </span>
        <span className="v num">
          {data.today.sessions}
          <small>{t('dashboard.unit.count')}</small>
        </span>
        <span className="s">
          {t('dashboard.tile.todayHint', { projects: data.today.projects, yesterday: data.today.yesterday })}
        </span>
      </button>
      <LimitTile />
      <button className="tile card" type="button" onClick={() => navigate('usage')}>
        <span className="k">
          <Icon name="bar-chart-line" />
          {t('dashboard.tile.week')}
          <b className="num total" title={exact(week)}>
            {tokens(week)}
          </b>
        </span>
        <span className="bars">
          {data.week.map((day, i) => (
            <span
              key={day.date}
              className={i === data.week.length - 1 ? 'today' : ''}
              style={{ height: `${Math.max(6, (day.tokens / max) * 100)}%` }}
              title={`${day.date} ${tokens(day.tokens)}`}
            />
          ))}
        </span>
      </button>
    </div>
  );
}

/** 계정 단위 구독 한도 가운데 가장 많이 쓴 7일(없으면 5시간) 창. 한도는 프로젝트와 상관없다 */
function LimitTile() {
  const limits = useLimits();
  const tools = useTools();
  const candidates = (limits.data?.tools ?? []).flatMap((tool) => tool.windows.map((window) => ({ tool, window })));
  const weekly = candidates.filter((c) => c.window.windowMinutes === 10080);
  const pool = weekly.length ? weekly : candidates;
  const pick = pool.reduce<(typeof pool)[number] | null>(
    (best, c) => (!best || c.window.usedPercent > best.window.usedPercent ? c : best),
    null,
  );
  const tool = pick ? tools.data?.find((x) => x.id === pick.tool.tool) : undefined;
  const used = pick ? Math.round(pick.window.usedPercent) : null;
  const windowKey = `dashboard.window.${pick?.window.windowMinutes ?? 0}`;

  return (
    <button className="tile card has-ring" type="button" onClick={() => navigate('usage')}>
      <span className="k">
        {tool ? <ToolBadge tool={tool} small /> : <Icon name="bar-chart-line" />}
        {pick && tool && has(windowKey)
          ? t('dashboard.tile.limit', { tool: tool.title, window: t(windowKey) })
          : t('dashboard.tile.limitAny')}
      </span>
      {used === null ? (
        <span className="s gap">{t('dashboard.tile.limitNone')}</span>
      ) : (
        <>
          <Ring percent={used} />
          <span className={`v alt num ${used >= 80 ? 'bad' : used >= 50 ? 'warn' : ''}`}>
            {used}
            <small>%</small>
          </span>
          {pick!.window.resetsAt && (
            <span className="s gap">
              {t('dashboard.tile.limitReset', { at: dateTime(pick!.window.resetsAt).slice(5) })}
            </span>
          )}
          {pick!.tool.at && (
            <span className="s faint">{t('dashboard.tile.limitAt', { at: dateTime(pick!.tool.at).slice(5) })}</span>
          )}
        </>
      )}
    </button>
  );
}

function Ring({ percent }: { percent: number }) {
  const r = 30;
  const c = 2 * Math.PI * r;
  const tone = percent >= 80 ? 'var(--bad)' : percent >= 50 ? 'var(--warn)' : 'var(--ink)';
  return (
    <svg className="ring" viewBox="0 0 80 80" aria-hidden="true">
      <circle cx="40" cy="40" r={r} fill="none" stroke="var(--line-soft)" strokeWidth="9" />
      <circle
        cx="40"
        cy="40"
        r={r}
        fill="none"
        stroke={tone}
        strokeWidth="9"
        strokeLinecap="round"
        strokeDasharray={`${(percent / 100) * c} ${c}`}
        transform="rotate(-90 40 40)"
      />
      <text x="40" y="45" textAnchor="middle" fontSize="15" fontWeight="700" fill="var(--ink)">
        {percent}%
      </text>
    </svg>
  );
}

// ── 최근 세션 ──

function RecentBox({ data, toolOf }: { data: Dashboard; toolOf: (id: string) => Tool | undefined }) {
  const resume = useResumeInRoom();
  return (
    <section className="card sum-box">
      <div className="box-head">
        <h3>{t('dashboard.recent')}</h3>
        <span className="faint sub">{t('dashboard.recentHint', { count: 5 })}</span>
        <span className="r">
          <button className="link" type="button" onClick={() => navigate('sessions')}>
            {t('dashboard.seeAll')}
          </button>
        </span>
      </div>
      <div className="sum-body">
        {!data.recent.length && <div className="faint pad">{t('dashboard.noSessions')}</div>}
        {data.recent.map(({ session, lastPrompt }) => {
          const tool = toolOf(session.tool);
          // 이어 붙인 세션은 첫 줄이 앞 대화 요약이라 제목이 없다. 그때는 마지막 질문을 제목 자리에 둔다
          const title = session.title || lastPrompt;
          return (
            <div className="srow3" key={session.path}>
              {tool ? <ToolBadge tool={tool} /> : <span />}
              <span className="t">
                <span className="ptag">{session.projectLabel}</span>
                <span className="ell" title={title ?? undefined}>
                  {title || <span className="faint">{t('sessions.noPrompt')}</span>}
                </span>
              </span>
              {session.title && (
                <span className="q ell" title={lastPrompt ?? undefined}>
                  <b>{t('dashboard.lastPrompt')}</b>
                  {lastPrompt ?? t('dashboard.noLastPrompt')}
                </span>
              )}
              <small className="num">
                {when(session.modifiedAt)} · {t('dashboard.turns', { count: session.userMessages })} ·{' '}
                {bytes(session.sizeBytes)}
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
      </div>
    </section>
  );
}

// ── 워크트리 ──

function WorktreeBox({ project }: { project: string | null }) {
  const worktrees = useWorktrees(project);
  const list = worktrees.data?.worktrees ?? [];
  const dirty = list.filter((item) => item.changes).length;

  return (
    <section className="card sum-box">
      <div className="box-head">
        <h3>{t('dashboard.worktrees')}</h3>
        {worktrees.data && (
          <span className="faint sub">{t('dashboard.worktreesHint', { count: list.length, dirty })}</span>
        )}
        <span className="r faint sub">{t('dashboard.worktreesOrder')}</span>
      </div>
      <div className="sum-body">
        {worktrees.isPending && <div className="faint pad">{t('dashboard.worktreesLoading')}</div>}
        {worktrees.isError && <div className="faint pad">{t('dashboard.worktreesFailed')}</div>}
        {worktrees.data?.gitMissing && <div className="faint pad">{t('dashboard.gitMissing')}</div>}
        {worktrees.data && !worktrees.data.gitMissing && !list.length && (
          <div className="faint pad">{t('dashboard.worktreesNone')}</div>
        )}
        {list.map((item) => (
          <WorktreeRow key={item.path} item={item} />
        ))}
      </div>
    </section>
  );
}

function distance(item: Worktree): string | null {
  if (!item.baseBranch || item.ahead == null || item.behind == null) return null;
  const base = item.baseBranch.replace(/^origin\//, '');
  if (!item.ahead && !item.behind) return t('dashboard.wt.same', { base });
  if (!item.ahead) return t('dashboard.wt.behind', { base, behind: item.behind });
  if (!item.behind) return t('dashboard.wt.ahead', { ahead: item.ahead });
  return t('dashboard.wt.both', { ahead: item.ahead, behind: item.behind });
}

function WorktreeRow({ item }: { item: Worktree }) {
  const far = distance(item);
  return (
    <div className="wrow">
      <span className="br">
        <span className="ptag">{item.repo}</span>
        <span className="ell mono" title={item.branch ?? undefined}>
          {item.branch ?? t('dashboard.wt.detached')}
        </span>
      </span>
      <span className="path ell mono" title={item.path}>
        {item.path}
      </span>
      <span className="cm ell" title={item.lastCommit ?? undefined}>
        {item.lastCommitAt && `${when(item.lastCommitAt)} · `}
        {item.lastCommit}
      </span>
      <span className="side">
        {!item.exists ? (
          <span className="pill bad">{t('dashboard.wt.gone')}</span>
        ) : item.changes ? (
          <span className="pill warn">{t('dashboard.wt.changes', { count: item.changes })}</span>
        ) : (
          <span className="pill ok">{t('dashboard.wt.clean')}</span>
        )}
        {far && <span className="ab">{far}</span>}
        {item.exists && (
          <button
            className="icon-btn"
            type="button"
            title={t('dashboard.wt.terminal')}
            onClick={() => {
              requestNewTerminal(item.path);
              navigate('terminal');
            }}
          >
            <Icon name="terminal" />
          </button>
        )}
      </span>
    </div>
  );
}

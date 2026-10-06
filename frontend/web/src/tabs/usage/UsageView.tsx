// 사용량 탭 (docs/design/daiso-d.html "사용량"). 맨 위 구독 한도(LimitsBox), 그 아래 토큰. $ 는 없다
import { useQuery } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { api, type Schemas } from '../../api/client';
import { exact, percent, tokens } from '../../format';
import { Icon } from '../../icons/Icon';
import { useIndexStatus, useRefreshIndex } from '../../indexStatus';
import { useCurrentProject, useSetCurrentProject } from '../../project';
import { t } from '../../strings';
import { ToolBadge, useTools } from '../../tools';
import { LimitsBox } from './LimitsBox';

type Grain = 'day' | 'week' | 'month';
type Usage = Schemas['UsageResponse'];

function useUsage(grain: Grain, tool: string | null, project: string | null) {
  return useQuery({
    // 첫 칸은 탭 id. 서버 알림 { tab: "usage" } 를 받으면 다시 받는다
    queryKey: ['usage', { grain, tool, project }],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/usage', {
        params: { query: { grain, tool: tool ?? undefined, project: project ?? undefined } },
      });
      if (error || !data) throw new Error('사용량을 읽지 못했다');
      return data;
    },
  });
}

export function UsageView() {
  const project = useCurrentProject();
  const setProject = useSetCurrentProject();
  const tools = useTools();
  const index = useIndexStatus();
  const refresh = useRefreshIndex();
  const [grain, setGrain] = useState<Grain>('day');
  const [tool, setTool] = useState<string | null>(null);
  // 프로젝트를 골랐어도 모든 프로젝트를 잠깐 볼 수 있다. 이 탭 안에서만이고 위 줄의 프로젝트는 그대로다
  const [allProjects, setAllProjects] = useState(false);
  const scope = project && !allProjects ? project.path : null;
  const usage = useUsage(grain, tool, scope);
  const running = index.data?.running ?? false;

  return (
    <>
      <div className="phead">
        <h2>{t('tab.usage')}</h2>
        {project && (
          <span className="seg" role="group" aria-label={t('usage.scope')}>
            <button type="button" aria-pressed={!allProjects} onClick={() => setAllProjects(false)}>
              <Icon name="folder" />
              {project.name}
            </button>
            <button type="button" aria-pressed={allProjects} onClick={() => setAllProjects(true)}>
              {t('top.project.all')}
            </button>
          </span>
        )}
        {usage.data?.firstDay && (
          <span className="state num" title={t('usage.utc')}>
            {usage.data.firstDay} ~ {usage.data.today}
          </span>
        )}
        <div className="acts">
          <button
            className="btn small"
            type="button"
            disabled={running}
            title={t('usage.refreshHint')}
            onClick={() => refresh.mutate()}
          >
            <Icon name="arrow-clockwise" />
            {running ? t('index.running') : t('usage.refresh')}
          </button>
        </div>
      </div>

      <div className="filters" role="group" aria-label={t('usage.tools')}>
        <button className="chip" type="button" aria-pressed={tool === null} onClick={() => setTool(null)}>
          {t('usage.allTools')}
        </button>
        {tools.data?.map((item) => (
          <button
            key={item.id}
            className="chip"
            type="button"
            aria-pressed={tool === item.id}
            onClick={() => setTool(item.id)}
          >
            <ToolBadge tool={item} small />
            {item.title}
          </button>
        ))}
      </div>

      {tools.data && <LimitsBox tools={tools.data} only={tool} />}
      {usage.isError && <div className="empty">{t('usage.failed')}</div>}
      {usage.data && (
        <Report
          usage={usage.data}
          grain={grain}
          onGrain={setGrain}
          scoped={!!scope}
          onPick={(path) => setProject.mutate(path)}
        />
      )}
    </>
  );
}

function Report({
  usage,
  grain,
  onGrain,
  scoped,
  onPick,
}: {
  usage: Usage;
  grain: Grain;
  onGrain: (grain: Grain) => void;
  scoped: boolean;
  onPick: (path: string) => void;
}) {
  const { totals } = usage;
  if (usage.buckets.every((bucket) => bucket.tokens.total === 0) && totals.last30Days.total === 0 && !usage.firstDay)
    return <div className="empty">{t('usage.empty')}</div>;

  return (
    <>
      <div className="cards3 num">
        <Total label={t('usage.today')} value={totals.today} />
        <Total label={t('usage.last7')} value={totals.last7Days} />
        <Total label={t('usage.last30')} value={totals.last30Days} />
      </div>

      <section className="card box">
        <div className="box-head">
          <h3>{t('usage.trend')}</h3>
          <span className="r">
            <span className="seg" role="group" aria-label={t('usage.grain')}>
              {(['day', 'week', 'month'] as const).map((value) => (
                <button key={value} type="button" aria-pressed={grain === value} onClick={() => onGrain(value)}>
                  {t(`usage.grain.${value}`)}
                </button>
              ))}
            </span>
          </span>
        </div>
        <Chart buckets={usage.buckets} grain={grain} />
      </section>

      <div className="two">
        {!scoped && (
          <section className="card box">
            <div className="box-head">
              <h3>{t('usage.byProject')}</h3>
            </div>
            <ShareTable
              rows={usage.byProject}
              head={t('usage.project')}
              render={(row) => (
                <button
                  className="link ell"
                  type="button"
                  title={`${row.key}\n${t('usage.pickProject')}`}
                  onClick={() => onPick(row.key)}
                >
                  {row.label}
                </button>
              )}
            />
          </section>
        )}
        <section className="card box">
          <div className="box-head">
            <h3>{t('usage.byModel')}</h3>
          </div>
          <ShareTable
            rows={usage.byModel}
            head={t('usage.model')}
            render={(row) => <span className="mono">{row.label}</span>}
          />
        </section>
      </div>
    </>
  );
}

function Total({ label, value }: { label: string; value: Schemas['UsageTokens'] }) {
  return (
    <div className="card" title={`${exact(value.total)}\n${breakdown(value)}`}>
      <span>{label}</span>
      <b>{tokens(value.total)}</b>
      <small>{t('usage.inOut', { input: tokens(value.input), output: tokens(value.output) })}</small>
    </div>
  );
}

function breakdown(value: Schemas['UsageTokens']): string {
  return t('usage.breakdown', {
    input: exact(value.input),
    output: exact(value.output),
    cacheCreate: exact(value.cacheCreate),
    cacheRead: exact(value.cacheRead),
  });
}

function ShareTable({
  rows,
  head,
  render,
}: {
  rows: Schemas['UsageShare'][];
  head: string;
  render: (row: Schemas['UsageShare']) => ReactNode;
}) {
  if (!rows.length) return <div className="faint">{t('usage.none')}</div>;
  return (
    <div className="tbl-wrap">
      <table className="tbl num">
        <thead>
          <tr>
            <th>{head}</th>
            <th className="r">{t('usage.tokens')}</th>
            <th className="r">{t('usage.share')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} title={breakdown(row.tokens)}>
              <td style={{ maxWidth: 260 }}>{render(row)}</td>
              <td className="r">{tokens(row.tokens.total)}</td>
              <td className="r">
                <span className="share">
                  <span style={{ width: percent(row.share) }} />
                </span>
                {percent(row.share)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** 막대 그래프. 마지막 칸(오늘이 든 칸)을 형광펜으로 칠한다 */
function Chart({ buckets, grain }: { buckets: Schemas['UsageBucket'][]; grain: Grain }) {
  const W = 760;
  const H = 210;
  const left = 44;
  const bottom = 24;
  const top = 10;
  const max = Math.max(1, ...buckets.map((bucket) => bucket.tokens.total)) * 1.1;
  const step = (W - left - 8) / Math.max(1, buckets.length);
  const width = Math.min(40, step * 0.66);
  const y = (v: number) => H - bottom - (v / max) * (H - bottom - top);
  const label = (start: string, i: number) => {
    const [, month, day] = start.split('-').map(Number);
    const last = i === buckets.length - 1;
    if (grain === 'month') return `${month}월`;
    if (grain === 'week') return i % 2 === buckets.length % 2 || last ? `${month}/${day}` : '';
    return last ? t('usage.today') : i % 5 === (buckets.length - 1) % 5 ? `${month}/${day}` : '';
  };

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={t('usage.trend')}>
      {[0, max / 2, max].map((v) => (
        <g key={v}>
          <line x1={left} x2={W - 8} y1={y(v)} y2={y(v)} stroke="var(--line-soft)" />
          <text x={left - 6} y={y(v) + 4} textAnchor="end">
            {tokens(Math.round(v))}
          </text>
        </g>
      ))}
      {buckets.map((bucket, i) => {
        const x = left + i * step + (step - width) / 2;
        const last = i === buckets.length - 1;
        const text = label(bucket.start, i);
        return (
          <g key={bucket.start}>
            <rect
              className="b"
              x={x}
              y={y(bucket.tokens.total)}
              width={width}
              height={Math.max(0, H - bottom - y(bucket.tokens.total))}
              rx={4}
              fill={last ? 'var(--hl)' : 'color-mix(in srgb, var(--ink) 18%, var(--surface))'}
              stroke={last ? 'var(--ink)' : undefined}
              strokeWidth={last ? 1.2 : undefined}
            >
              <title>{`${bucket.start} · ${exact(bucket.tokens.total)}\n${breakdown(bucket.tokens)}`}</title>
            </rect>
            {text && (
              <text x={x + width / 2} y={H - 6} textAnchor="middle">
                {text}
              </text>
            )}
          </g>
        );
      })}
    </svg>
  );
}

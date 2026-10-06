// 구독 한도 (docs/design/daiso-d.html "구독 한도"). 계정 단위라 프로젝트와 상관없다
// 값은 도구가 남긴 파일에서 온다. 탭을 열면 읽고, 파일이 바뀌면 서버 알림 { tab: "limits" } 로 다시 받는다
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api, type Schemas } from '../../api/client';
import { Icon } from '../../icons/Icon';
import { t } from '../../strings';
import { ToolBadge, type Tool } from '../../tools';

type Limits = Schemas['ToolLimits'];
type Window = Schemas['LimitWindow'];

/** 5시간 칸과 7일 칸. 도구가 한쪽만 알려 주면 다른 칸은 비워 둔다 */
const COLUMNS = [300, 10080];

export function useLimits() {
  return useQuery({
    queryKey: ['limits'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/limits');
      if (error || !data) throw new Error('구독 한도를 읽지 못했다');
      return data;
    },
    // 탭을 열 때마다 파일을 다시 읽는다. 서버를 띄운 뒤 도구를 쓴 만큼 바뀌어 있을 수 있다
    staleTime: 0,
    refetchOnMount: 'always',
  });
}

function useSetStatusLine() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (enabled: boolean) => {
      const { data, error } = await api.PUT('/api/limits/claude/statusline', { body: { enabled } });
      if (error || !data)
        throw new Error((error as { detail?: string } | undefined)?.detail ?? 'Claude 설정 파일을 고치지 못했다');
      return data;
    },
    onSuccess: (data) => queryClient.setQueryData(['limits'], data),
  });
}

/** 지금 시각. 1분마다 바꿔 "몇 분 뒤 초기화"와 "초기화됨"이 저절로 맞춰지게 한다 */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(id);
  }, []);
  return now;
}

export function LimitsBox({ tools, only }: { tools: Tool[]; only: string | null }) {
  const now = useNow();
  const limits = useLimits();
  const setStatusLine = useSetStatusLine();
  const rows = (limits.data?.tools ?? []).filter((row) => !only || row.tool === only);
  const claudeOn = limits.data?.tools.find((row) => row.tool === 'claude')?.enabled === true;
  if (!rows.length) return null;

  return (
    <section className="card limits">
      <div className="box-head">
        <h3>{t('limits.title')}</h3>
        <span className="lnote">{t('limits.account')}</span>
        {claudeOn && (!only || only === 'claude') && (
          <span className="r">
            <button
              className="link"
              type="button"
              title={t('limits.offHint')}
              disabled={setStatusLine.isPending}
              onClick={() => setStatusLine.mutate(false)}
            >
              {t('limits.off')}
            </button>
          </span>
        )}
      </div>
      {setStatusLine.isError && <div className="lnote err">{setStatusLine.error.message}</div>}
      {rows.map((row) => {
        const tool = tools.find((item) => item.id === row.tool);
        return (
          <div className="lrow" key={row.tool}>
            {tool ? <ToolBadge tool={tool} /> : <span />}
            <span className="name">
              {tool?.title ?? row.tool}
              {row.at && (
                <small className="lnote" title={t('limits.atHint')}>
                  {t('limits.at', { when: when(row.at, now) })}
                </small>
              )}
            </span>
            {/* 켜고 끌 때마다 새로 그린다. 안 그러면 '켜기 확인' 단계가 남아 다음에 끄고 나서 바로 확인 단계가 뜬다 */}
            <Row
              key={String(row.enabled)}
              row={row}
              now={now}
              onEnable={() => setStatusLine.mutate(true)}
              pending={setStatusLine.isPending}
            />
          </div>
        );
      })}
    </section>
  );
}

function Row({ row, now, onEnable, pending }: { row: Limits; now: number; onEnable: () => void; pending: boolean }) {
  const [asking, setAsking] = useState(false);

  if (row.source === 'none') return <span className="lnote">{t('limits.unknownTool')}</span>;

  if (row.source === 'statusline' && !row.enabled)
    return asking ? (
      <span className="consent">
        <span>{t('limits.consent')}</span>
        <span className="with-btn">
          <button className="btn small primary" type="button" disabled={pending} onClick={onEnable}>
            <Icon name="check2" />
            {t('limits.enable')}
          </button>
          <button className="btn small" type="button" onClick={() => setAsking(false)}>
            {t('limits.cancel')}
          </button>
        </span>
      </span>
    ) : (
      <span className="with-btn" style={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <span className="muted" style={{ flex: 1, minWidth: 200 }}>
          {t('limits.claudeOff')}
        </span>
        <button className="btn small" type="button" onClick={() => setAsking(true)}>
          <Icon name="eye" />
          {t('limits.enableAsk')}
        </button>
      </span>
    );

  if (!row.windows.length)
    return <span className="lnote">{row.source === 'statusline' ? t('limits.waitClaude') : t('limits.noRecord')}</span>;

  return (
    <span className="lbars">
      {COLUMNS.map((minutes) => {
        const window = row.windows.find((item) => item.windowMinutes === minutes);
        return window ? <Bar key={minutes} window={window} now={now} /> : <Missing key={minutes} minutes={minutes} />;
      })}
      {/* 5시간·7일 말고 다른 창을 알려 주는 도구도 있다. 그런 창은 뒤에 붙인다 */}
      {row.windows
        .filter((item) => !COLUMNS.includes(item.windowMinutes))
        .map((item) => (
          <Bar key={item.windowMinutes} window={item} now={now} />
        ))}
    </span>
  );
}

function Bar({ window, now }: { window: Window; now: number }) {
  const reset = window.resetsAt ? new Date(window.resetsAt) : null;
  // 초기화 시각이 지났으면 그 뒤로는 안 썼다는 뜻이다. 0 으로 보여 준다
  const passed = reset !== null && reset.getTime() <= now;
  const used = passed ? 0 : window.usedPercent;
  const level = used >= 80 ? 'bad' : used >= 50 ? 'warn' : '';
  return (
    <span className="lbar">
      <span className="head">
        <span>{span(window.windowMinutes)}</span>
        <b className="num">{Math.round(used)}%</b>
        <small>
          {passed ? t('limits.resetDone') : reset ? t('limits.resetAt', { when: resetText(reset, now) }) : ''}
        </small>
      </span>
      <span className={`track ${level}`}>
        <span style={{ width: `${Math.min(100, used)}%` }} />
      </span>
    </span>
  );
}

function Missing({ minutes }: { minutes: number }) {
  return (
    <span className="lbar">
      <span className="head">
        <span>{span(minutes)}</span>
        <small>{t('limits.notReported')}</small>
      </span>
      <span className="track" style={{ opacity: 0.5 }} />
    </span>
  );
}

/** 300 → 5시간, 10080 → 7일 */
function span(minutes: number): string {
  if (minutes % 1440 === 0) return t('limits.days', { n: minutes / 1440 });
  if (minutes % 60 === 0) return t('limits.hours', { n: minutes / 60 });
  return t('limits.minutes', { n: minutes });
}

/** 하루 안이면 "2시간 10분 뒤", 아니면 "10-09 (목) 09:00" */
function resetText(at: Date, now: number): string {
  const minutes = Math.round((at.getTime() - now) / 60_000);
  if (minutes < 24 * 60) {
    const h = Math.floor(minutes / 60);
    const m = minutes % 60;
    return h ? t('limits.inHoursMinutes', { h, m }) : t('limits.inMinutes', { m });
  }
  return dayTime(at, true);
}

/** 오늘이면 "오늘 14:20", 아니면 "10-03 17:40" */
function when(iso: string, now: number): string {
  const at = new Date(iso);
  return at.toDateString() === new Date(now).toDateString() ? `${t('usage.today')} ${hm(at)}` : dayTime(at, false);
}

function dayTime(at: Date, weekday: boolean): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const day = `${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
  return weekday ? `${day} (${'일월화수목금토'[at.getDay()]}) ${hm(at)}` : `${day} ${hm(at)}`;
}

function hm(at: Date): string {
  return `${String(at.getHours()).padStart(2, '0')}:${String(at.getMinutes()).padStart(2, '0')}`;
}

// 위 줄 계정 단추 (docs/design/daiso-d.html "계정"). 도구마다 로그인 상태, 다시 로그인, 저장한 계정
// 로그인은 앱이 하지 않는다. 새 터미널 창에서 도구의 로그인 명령을 띄울 뿐이다. 토큰 값은 화면에 오지 않는다
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { api, type Schemas } from './api/client';
import { dateTime } from './format';
import { Icon } from './icons/Icon';
import { has, t, type StringKey } from './strings';
import { ToolBadge, useTools, type Tool } from './tools';

export type Account = Schemas['Account'];

export function useAccounts() {
  return useQuery({
    queryKey: ['accounts'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/accounts');
      if (error || !data) throw new Error('계정 상태를 읽지 못했다');
      return data;
    },
  });
}

export function useLogin() {
  return useMutation({
    mutationFn: async (tool: string) => {
      const { error } = await api.POST('/api/accounts/{tool}/login', { params: { path: { tool } } });
      if (error) throw new Error('로그인 창을 띄우지 못했다');
    },
  });
}

type ProfileAction = 'save' | 'apply' | 'remove';

function useProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ tool, name, action }: { tool: string; name: string; action: ProfileAction }) => {
      const path = { tool };
      const body = { name };
      const { error } =
        action === 'save'
          ? await api.POST('/api/accounts/{tool}/profiles', { params: { path }, body })
          : action === 'apply'
            ? await api.POST('/api/accounts/{tool}/profiles/apply', { params: { path }, body })
            : await api.POST('/api/accounts/{tool}/profiles/remove', { params: { path }, body });
      if (error) throw new Error((error as { title?: string }).title ?? '계정을 다루지 못했다');
      return action;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts'] }),
  });
}

/** 상태 → 점 색 */
const DOT: Record<string, string> = { loggedIn: 'ok', expiringSoon: 'warn', expired: 'bad', missing: 'bad' };

export function AccountButton() {
  const accounts = useAccounts();
  const tools = useTools();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [open]);

  const toolOf = (id: string) => tools.data?.find((tool) => tool.id === id);

  return (
    <span className="anchor" ref={box}>
      <button
        className="acctbtn"
        type="button"
        aria-expanded={open}
        title={t('top.account')}
        onClick={() => setOpen(!open)}
      >
        <span className="tools">
          {accounts.data?.map((account) => {
            const tool = toolOf(account.tool);
            return tool ? (
              <span key={account.tool} className="badge-dot">
                <ToolBadge tool={tool} small />
                <i className={`st ${account.installed ? DOT[account.state] : 'off'}`} />
              </span>
            ) : null;
          })}
        </span>
        <span className="lbl">{t('top.account')}</span>
      </button>
      {open && (
        <div className="pop right acct-list">
          {accounts.data?.map((account) => (
            <AccountRow key={account.tool} account={account} tool={toolOf(account.tool)} />
          ))}
          {accounts.isPending && <div className="pop-note">{t('top.project.loading')}</div>}
        </div>
      )}
    </span>
  );
}

function AccountRow({ account, tool }: { account: Account; tool?: Tool }) {
  const login = useLogin();
  const profile = useProfile();
  const [expanded, setExpanded] = useState(false);
  const [name, setName] = useState('');
  const state = account.installed ? account.state : 'notInstalled';

  return (
    <div className="arow2">
      {tool ? <ToolBadge tool={tool} /> : <span />}
      <span>
        <b>{tool?.title ?? account.tool}</b>{' '}
        <span className={`pill ${account.installed ? (DOT[account.state] ?? 'plain') : 'plain'}`}>
          {t(`account.state.${state}` as StringKey)}
        </span>
      </span>
      <span className="meta ell">{summary(account)}</span>
      <span className="acts">
        {account.installed && (
          <button
            className={`btn small ${account.state === 'loggedIn' ? '' : 'primary'}`}
            type="button"
            title={t('account.loginHint')}
            disabled={login.isPending}
            onClick={() => login.mutate(account.tool)}
          >
            <Icon name="box-arrow-in-right" />
            {account.state === 'missing' ? t('account.login') : t('account.relogin')}
          </button>
        )}
        <button
          className="icon-btn"
          type="button"
          aria-pressed={expanded}
          title={t('account.more')}
          onClick={() => setExpanded(!expanded)}
        >
          <Icon name="people" />
        </button>
      </span>
      {expanded && (
        <div className="sub">
          {account.canSaveProfiles ? (
            <>
              <p>{t('account.saveHint')}</p>
              <span className="with-btn">
                <input
                  className="input"
                  value={name}
                  placeholder={t('account.profileName')}
                  onChange={(e) => setName(e.target.value)}
                />
                <button
                  className="btn small primary"
                  type="button"
                  disabled={!name.trim() || profile.isPending}
                  onClick={() =>
                    profile.mutate({ tool: account.tool, name, action: 'save' }, { onSuccess: () => setName('') })
                  }
                >
                  {t('account.saveNow')}
                </button>
              </span>
            </>
          ) : (
            <p>{t('account.cannotSave')}</p>
          )}
          {account.profiles.map((saved) => (
            <div className="prof" key={saved.name}>
              <b>{saved.name}</b>
              <button
                className="btn small"
                type="button"
                disabled={profile.isPending}
                onClick={() => profile.mutate({ tool: account.tool, name: saved.name, action: 'apply' })}
              >
                {t('account.useThis')}
              </button>
              <button
                className="icon-btn"
                type="button"
                title={t('account.removeProfile')}
                disabled={profile.isPending}
                onClick={() => profile.mutate({ tool: account.tool, name: saved.name, action: 'remove' })}
              >
                <Icon name="trash3" />
              </button>
              <small>
                {saved.accountLabel ?? saved.email ?? t('account.noInfo')} ·{' '}
                {t('account.savedAt', { at: dateTime(saved.savedAt) })}
              </small>
            </div>
          ))}
          {profile.isSuccess && <p className="ok-note">{t(`account.done.${profile.data}` as StringKey)}</p>}
          {profile.isError && <p className="lnote err">{profile.error.message}</p>}
          {account.notes.length > 0 && (
            <ul className="notes">
              {account.notes.map((note, i) => (
                <li key={i}>{noteText(note)}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

/** 요금제 · 만료. 없으면 "정보 없음" 대신 비워 둔다 */
function summary(account: Account): string {
  if (!account.installed) return t('account.notInstalledHint');
  const parts = [account.accountLabel ?? account.email, account.plan && t('account.plan', { plan: account.plan })];
  if (account.expiresAt) parts.push(t('account.until', { at: dateTime(account.expiresAt).slice(0, 10) }));
  const text = parts.filter(Boolean).join(' · ');
  return text || t('account.noInfo');
}

/** 도구가 준 문구 키로 문장을 찾는다. 모르는 키(플러그인 등)는 키와 값을 그대로 보인다 */
function noteText(note: Schemas['AccountNote']): string {
  const key = `authNote.${note.key}`;
  if (!has(key)) return `${note.key}${note.argument ? `: ${note.argument}` : ''}`;
  let argument = note.argument ?? '';
  // 마지막 갱신은 시각이다. 키로만 가린다(값이 날짜처럼 보인다고 바꾸지 않는다. "5" 가 5월로 읽힌 적이 있다)
  if (note.key === 'AuthNote_LastRefresh' && argument) argument = dateTime(argument);
  return t(key, { 0: argument });
}

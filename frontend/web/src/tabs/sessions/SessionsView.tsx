// 세션 탭 (docs/design/daiso-d.html "세션"). 왼쪽 목록, 오른쪽 대화. 프로젝트는 위 줄에서 고른 것을 따른다
import { useEffect, useRef, useState } from 'react';
import { bytes, dateTime } from '../../format';
import { Icon } from '../../icons/Icon';
import { useCurrentProject } from '../../project';
import { t } from '../../strings';
import { ToolBadge, useTools, type Tool } from '../../tools';
import { useIndexStatus, useRefreshIndex } from '../../indexStatus';
import {
  DEFAULT_FILTER,
  exportUrl,
  useDelete,
  useMessages,
  useRename,
  useResume,
  useSearch,
  useSessions,
  useSetCleanup,
  clearPendingOpen,
  peekPendingOpen,
  type Session,
} from './api';

type Pop = 'filter' | 'pick' | null;

export function SessionsView() {
  const project = useCurrentProject();
  const tools = useTools();
  const index = useIndexStatus();
  const refresh = useRefreshIndex();
  const [filter, setFilter] = useState(DEFAULT_FILTER);
  const [query, setQuery] = useState('');
  const [searched, setSearched] = useState('');
  const [selected, setSelected] = useState<string | null>(peekPendingOpen);
  useEffect(() => clearPendingOpen(), []);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [pop, setPop] = useState<Pop>(null);
  const [deleting, setDeleting] = useState(false);
  const [note, setNote] = useState<string | null>(null);

  const list = useSessions({ ...filter, project: project?.path ?? null });
  const search = useSearch(searched);
  const sessions = list.data?.sessions ?? [];
  const current =
    sessions.find((s) => s.path === selected) ??
    search.data?.groups.find((g) => g.session.path === selected)?.session ??
    null;
  const toolOf = (id: string) => tools.data?.find((tool) => tool.id === id);
  const searching = searched.length >= 2;

  const toggle = (path: string, on: boolean) =>
    setChecked((old) => {
      const next = new Set(old);
      if (on) next.add(path);
      else next.delete(path);
      return next;
    });

  const runSearch = () => {
    const q = query.trim();
    if (q.length < 2) {
      setNote(t('sessions.queryTooShort'));
      return;
    }
    setNote(null);
    setSearched(q);
  };

  const pickBy = (rule: 'old' | 'orphan' | 'large') => {
    const cleanup = list.data?.cleanup;
    if (!cleanup) return;
    const cutoff = Date.now() - cleanup.olderThanDays * 86_400_000;
    const picked = sessions.filter((s) =>
      rule === 'old'
        ? new Date(s.modifiedAt).getTime() < cutoff
        : rule === 'orphan'
          ? s.orphan
          : s.sizeBytes > cleanup.largerThanMegabytes * 1024 * 1024,
    );
    setChecked((old) => new Set([...old, ...picked.map((s) => s.path)]));
    setPop(null);
  };

  const totalSize = sessions.reduce((sum, s) => sum + s.sizeBytes, 0);

  return (
    <>
      <div className="sess-top">
        <div className="phead">
          <h2>{t('tab.sessions')}</h2>
          <span className="state num">
            {project ? `${project.name} · ` : ''}
            {t('sessions.count', { count: sessions.length, size: bytes(totalSize) })}
          </span>
          <div className="acts">
            <button
              className="btn small"
              type="button"
              disabled={index.data?.running}
              onClick={() => refresh.mutate()}
              title={t('usage.refreshHint')}
            >
              <Icon name="arrow-clockwise" />
              {index.data?.running ? t('index.running') : t('usage.refresh')}
            </button>
          </div>
        </div>
        <div className="filters" role="group" aria-label={t('usage.tools')}>
          <button
            className="chip"
            type="button"
            aria-pressed={filter.tool === null}
            onClick={() => setFilter({ ...filter, tool: null })}
          >
            {t('usage.allTools')}
          </button>
          {tools.data?.map((tool) => (
            <button
              key={tool.id}
              className="chip"
              type="button"
              aria-pressed={filter.tool === tool.id}
              onClick={() => setFilter({ ...filter, tool: tool.id })}
            >
              <ToolBadge tool={tool} small />
              {tool.title}
            </button>
          ))}
        </div>
        <div className="filters">
          <div className="searchbox">
            <span className="wrap">
              <Icon name="search" />
              <input
                className="input"
                placeholder={t('sessions.searchHint')}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') runSearch();
                  if (e.key === 'Escape') {
                    setQuery('');
                    setSearched('');
                  }
                }}
                aria-label={t('sessions.searchHint')}
              />
            </span>
            <button className="btn small primary" type="button" onClick={runSearch}>
              {t('sessions.search')}
            </button>
            {searched && (
              <button
                className="icon-btn"
                type="button"
                title={t('sessions.clearSearch')}
                onClick={() => {
                  setQuery('');
                  setSearched('');
                }}
              >
                <Icon name="x-lg" />
              </button>
            )}
          </div>
          <select
            className="select pill-select"
            value={filter.days}
            onChange={(e) => setFilter({ ...filter, days: Number(e.target.value) })}
            aria-label={t('sessions.period')}
          >
            {[0, 7, 30, 90].map((days) => (
              <option key={days} value={days}>
                {days ? t('sessions.lastDays', { days }) : t('sessions.allTime')}
              </option>
            ))}
          </select>
          <span className="anchor">
            <button className="chip" type="button" onClick={() => setPop(pop === 'filter' ? null : 'filter')}>
              <Icon name="funnel" />
              {t('sessions.moreFilters')}
            </button>
            {pop === 'filter' && (
              <div className="pop pop-form">
                <label className="sfield">
                  <span>{t('sessions.minSize')}</span>
                  <input
                    className="input num-in"
                    type="number"
                    min={0}
                    value={filter.minMegabytes}
                    onChange={(e) => setFilter({ ...filter, minMegabytes: Math.max(0, Number(e.target.value)) })}
                  />
                </label>
                <label className="inline">
                  <input
                    type="checkbox"
                    checked={filter.orphans}
                    onChange={(e) => setFilter({ ...filter, orphans: e.target.checked })}
                  />
                  {t('sessions.orphansOnly')}
                </label>
                <label className="inline">
                  <input
                    type="checkbox"
                    checked={filter.archived}
                    onChange={(e) => setFilter({ ...filter, archived: e.target.checked })}
                  />
                  {t('sessions.includeArchived')}
                </label>
                <button className="btn small" type="button" onClick={() => setFilter(DEFAULT_FILTER)}>
                  {t('sessions.resetFilters')}
                </button>
              </div>
            )}
          </span>
          {note && <span className="lnote err">{note}</span>}
        </div>
      </div>

      <div className={`split ${current ? 'has-sel' : ''}`}>
        <div className="list-pane">
          {checked.size > 0 ? (
            <div className="list-head sel">
              <button
                className="icon-btn"
                type="button"
                title={t('sessions.clearChecks')}
                onClick={() => setChecked(new Set())}
              >
                <Icon name="x-lg" />
              </button>
              <b>{t('sessions.checked', { count: checked.size })}</b>
              <span className="grow" />
              <button className="btn small danger" type="button" onClick={() => setDeleting(true)}>
                <Icon name="trash3" />
                {t('sessions.delete')}
              </button>
            </div>
          ) : (
            <div className="list-head">
              {!searching && (
                <input
                  type="checkbox"
                  aria-label={t('sessions.checkAll')}
                  title={t('sessions.checkAll')}
                  onChange={(e) => sessions.forEach((s) => toggle(s.path, e.target.checked))}
                />
              )}
              <b>{searching ? t('sessions.searchResults') : t('sessions.list')}</b>
              <span className="faint num" style={{ fontSize: 'var(--t-xs)' }}>
                {searching ? t('sessions.hits', { count: search.data?.total ?? 0 }) : sessions.length}
              </span>
              <span className="grow" />
              {!searching && list.data && (
                <span className="anchor">
                  <button
                    className="btn small quiet"
                    type="button"
                    onClick={() => setPop(pop === 'pick' ? null : 'pick')}
                  >
                    <Icon name="check2-square" />
                    {t('sessions.pick')}
                    <Icon name="chevron-down" />
                  </button>
                  {pop === 'pick' && (
                    <PickMenu cleanup={list.data.cleanup} onPick={pickBy} onClose={() => setPop(null)} />
                  )}
                </span>
              )}
            </div>
          )}
          {searching ? (
            <SearchTree
              groups={search.data?.groups ?? []}
              loading={search.isPending}
              toolOf={toolOf}
              onOpen={setSelected}
            />
          ) : (
            <ul className="slist">
              {list.isPending && <li className="centered">{t('top.project.loading')}</li>}
              {list.isSuccess && !sessions.length && <li className="centered">{t('sessions.none')}</li>}
              {sessions.map((s) => (
                <Row
                  key={s.path}
                  session={s}
                  tool={toolOf(s.tool)}
                  showProject={!project}
                  selected={s.path === selected}
                  checked={checked.has(s.path)}
                  onCheck={(on) => toggle(s.path, on)}
                  onOpen={() => setSelected(s.path)}
                />
              ))}
            </ul>
          )}
        </div>
        <div className="detail">
          {current ? (
            <Detail key={current.path} session={current} tool={toolOf(current.tool)} onBack={() => setSelected(null)} />
          ) : (
            <div className="centered">{t('sessions.pickOne')}</div>
          )}
        </div>
      </div>

      {deleting && (
        <DeleteDialog
          sessions={sessions.filter((s) => checked.has(s.path))}
          onClose={(done) => {
            setDeleting(false);
            if (done) setChecked(new Set());
          }}
        />
      )}
    </>
  );
}

function Row({
  session,
  tool,
  showProject,
  selected,
  checked,
  onCheck,
  onOpen,
}: {
  session: Session;
  tool?: Tool;
  showProject: boolean;
  selected: boolean;
  checked: boolean;
  onCheck: (on: boolean) => void;
  onOpen: () => void;
}) {
  return (
    <li className="srow" aria-selected={selected} onClick={onOpen}>
      <input
        type="checkbox"
        checked={checked}
        onClick={(e) => e.stopPropagation()}
        onChange={(e) => onCheck(e.target.checked)}
        aria-label={t('sessions.check')}
      />
      {tool ? <ToolBadge tool={tool} /> : <span />}
      <span className={`t ell ${session.title ? '' : 'empty-title'}`} title={session.title}>
        {session.named && <Icon name="pencil" />} {session.title || t('sessions.noPrompt')}
      </span>
      <span className="m num">
        {showProject && <span title={session.projectPath ?? ''}>{session.projectLabel}</span>}
        <span>{dateTime(session.modifiedAt)}</span>
        <span>{bytes(session.sizeBytes)}</span>
        <span title={t('sessions.messageCounts', { user: session.userMessages, assistant: session.assistantMessages })}>
          <Icon name="chat-left-text" /> {session.userMessages}
        </span>
        {session.active && <span className="pill ok">{t('sessions.active')}</span>}
        {session.archived && <span className="pill plain">{t('sessions.archived')}</span>}
        {session.orphan && session.projectPath && <span className="pill bad">{t('sessions.folderMissing')}</span>}
      </span>
    </li>
  );
}

function SearchTree({
  groups,
  loading,
  toolOf,
  onOpen,
}: {
  groups: { session: Session; matches: { role: string; at: string; snippet: string }[] }[];
  loading: boolean;
  toolOf: (id: string) => Tool | undefined;
  onOpen: (path: string) => void;
}) {
  if (loading) return <div className="centered">{t('sessions.searching')}</div>;
  if (!groups.length) return <div className="centered">{t('sessions.noHits')}</div>;
  // 프로젝트 > 세션 > 걸린 메시지
  const byProject = new Map<string, typeof groups>();
  for (const group of groups) {
    const key = group.session.projectLabel;
    byProject.set(key, [...(byProject.get(key) ?? []), group]);
  }
  return (
    <ul className="tree">
      {[...byProject.entries()].map(([label, items]) => (
        <li key={label}>
          <div className="proj">
            <Icon name="folder" />
            {label}
            <span className="faint" style={{ fontWeight: 500, fontSize: 'var(--t-xs)' }}>
              · {t('sessions.sessionCount', { count: items.length })}
            </span>
          </div>
          {items.map(({ session, matches }) => {
            const tool = toolOf(session.tool);
            return (
              <div key={session.path}>
                <div className="tsess" onClick={() => onOpen(session.path)}>
                  {tool && <ToolBadge tool={tool} small />}
                  <b className="ell">{session.title || t('sessions.noPrompt')}</b>
                  <small className="faint">
                    {dateTime(session.modifiedAt)} · {t('sessions.hits', { count: matches.length })}
                  </small>
                </div>
                {matches.map((match, i) => (
                  <div key={i} className="hit" onClick={() => onOpen(session.path)}>
                    [{t(`role.${match.role}` as 'role.user')}] {match.snippet.replace(/\s+/g, ' ')}
                  </div>
                ))}
              </div>
            );
          })}
        </li>
      ))}
    </ul>
  );
}

function Detail({ session, tool, onBack }: { session: Session; tool?: Tool; onBack: () => void }) {
  const [showTools, setShowTools] = useState(false);
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(session.named ? session.title : '');
  const messages = useMessages(session.path, showTools);
  const rename = useRename();
  const resume = useResume();
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (editing) input.current?.focus();
  }, [editing]);

  const save = () => {
    setEditing(false);
    rename.mutate({ path: session.path, name: name.trim() || null });
  };

  return (
    <>
      <div className="dtools">
        <button className="link back-list" type="button" onClick={onBack}>
          <Icon name="chevron-left" />
          {t('sessions.backToList')}
        </button>
        {tool && <ToolBadge tool={tool} />}
        {editing ? (
          <input
            ref={input}
            className="input title-edit"
            value={name}
            placeholder={t('sessions.namePlaceholder')}
            onChange={(e) => setName(e.target.value)}
            onBlur={save}
            onKeyDown={(e) => {
              if (e.key === 'Enter') save();
              if (e.key === 'Escape') setEditing(false);
            }}
          />
        ) : (
          <b className="ell" style={{ maxWidth: 340 }} title={session.title}>
            {session.title || t('sessions.noPrompt')}
          </b>
        )}
        <button className="icon-btn" type="button" title={t('sessions.rename')} onClick={() => setEditing(true)}>
          <Icon name="pencil" />
        </button>
        <button
          className="btn small primary"
          type="button"
          title={t('sessions.resumeHint')}
          disabled={resume.isPending}
          onClick={() => resume.mutate(session.path)}
        >
          <Icon name="arrow-repeat" />
          {t('sessions.resume')}
        </button>
        <a className="btn small" href={exportUrl(session.path)} download>
          <Icon name="file-earmark-text" />
          {t('sessions.export')}
        </a>
        <label>
          <input type="checkbox" checked={showTools} onChange={(e) => setShowTools(e.target.checked)} />
          {t('sessions.showTools')}
        </label>
        {resume.isError && <span className="lnote err">{resume.error.message}</span>}
      </div>
      <div className="timeline">
        {messages.isPending && <div className="centered">{t('sessions.reading')}</div>}
        {messages.isError && <div className="centered">{messages.error.message}</div>}
        {messages.data?.messages.map((message, i) => (
          <Message key={i} {...message} />
        ))}
        {messages.data?.truncated && <div className="lnote">{t('sessions.truncated')}</div>}
      </div>
    </>
  );
}

/** 1500자에서 접는다. 옛 화면과 같다 */
const PREVIEW = 1500;

function Message({ role, at, text }: { role: string; at: string; text: string }) {
  const [open, setOpen] = useState(false);
  const long = text.length > PREVIEW;
  return (
    <div className={`tl ${role}`}>
      <span className="role">
        {t(`role.${role}` as 'role.user')}
        <small className="num">{dateTime(at, true)}</small>
      </span>
      <span className="txt">
        {long && !open ? `${text.slice(0, PREVIEW)} …` : text}
        {long && (
          <>
            {' '}
            <button className="link" type="button" onClick={() => setOpen(!open)}>
              {open ? t('sessions.showLess') : t('sessions.showMore', { count: text.length - PREVIEW })}
            </button>
          </>
        )}
      </span>
    </div>
  );
}

function PickMenu({
  cleanup,
  onPick,
  onClose,
}: {
  cleanup: { olderThanDays: number; largerThanMegabytes: number };
  onPick: (rule: 'old' | 'orphan' | 'large') => void;
  onClose: () => void;
}) {
  const setCleanup = useSetCleanup();
  const [days, setDays] = useState(cleanup.olderThanDays);
  const [mb, setMb] = useState(cleanup.largerThanMegabytes);
  const changed = days !== cleanup.olderThanDays || mb !== cleanup.largerThanMegabytes;
  return (
    <div className="pop right" style={{ width: 280 }}>
      <button className="menu-item" type="button" onClick={() => onPick('old')}>
        <Icon name="clock" />
        {t('sessions.pickOld', { days: cleanup.olderThanDays })}
      </button>
      <button className="menu-item" type="button" onClick={() => onPick('orphan')}>
        <Icon name="folder" />
        {t('sessions.pickOrphan')}
      </button>
      <button className="menu-item" type="button" onClick={() => onPick('large')}>
        <Icon name="hdd" />
        {t('sessions.pickLarge', { mb: cleanup.largerThanMegabytes })}
      </button>
      <div className="menu-sep" />
      <div className="pop-form" style={{ width: 'auto', padding: '6px 10px' }}>
        <span className="lnote">{t('sessions.ruleHint')}</span>
        <span className="inline">
          <input
            className="input num-in"
            type="number"
            min={1}
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          />
          {t('sessions.days')}
          <input
            className="input num-in"
            type="number"
            min={1}
            value={mb}
            onChange={(e) => setMb(Number(e.target.value))}
          />
          MB
        </span>
        <button
          className="btn small"
          type="button"
          disabled={!changed || days < 1 || mb < 1 || setCleanup.isPending}
          onClick={() => setCleanup.mutate({ olderThanDays: days, largerThanMegabytes: mb }, { onSuccess: onClose })}
        >
          {t('sessions.saveRule')}
        </button>
      </div>
    </div>
  );
}

function DeleteDialog({ sessions, onClose }: { sessions: Session[]; onClose: (done: boolean) => void }) {
  const remove = useDelete();
  const [permanent, setPermanent] = useState(false);
  const running = sessions.filter((s) => s.active);
  const size = sessions.filter((s) => !s.active).reduce((sum, s) => sum + s.sizeBytes, 0);
  const result = remove.data;

  return (
    <div className="modal-bg" onClick={(e) => e.target === e.currentTarget && onClose(!!result)}>
      <div className="modal" role="dialog" aria-label={t('sessions.delete')}>
        <header>
          <h3>
            {result
              ? t('sessions.deleted', { count: result.deleted.length })
              : t('sessions.deleteTitle', { count: sessions.length - running.length })}
          </h3>
        </header>
        <div className="mbody">
          {result ? (
            result.skipped.length > 0 && (
              <>
                <span>{t('sessions.skipped', { count: result.skipped.length })}</span>
                <ul className="skipped">
                  {result.skipped.map((skip) => (
                    <li key={skip.path}>
                      {skip.path.split(/[\\/]/).pop()} — {skip.reason}
                    </li>
                  ))}
                </ul>
              </>
            )
          ) : (
            <>
              <span className="num">
                {t('sessions.reclaim', { size: bytes(size) })}
                {running.length > 0 && ` · ${t('sessions.runningExcluded', { count: running.length })}`}
              </span>
              <label className="check">
                <input type="checkbox" checked={permanent} onChange={(e) => setPermanent(e.target.checked)} />
                <span>
                  <b>{t('sessions.permanent')}</b>
                  <small>{t('sessions.permanentHint')}</small>
                </span>
              </label>
            </>
          )}
          {remove.isError && <span className="lnote err">{remove.error.message}</span>}
        </div>
        <footer>
          {result ? (
            <button className="btn primary" type="button" onClick={() => onClose(true)}>
              {t('sessions.close')}
            </button>
          ) : (
            <>
              <button className="btn" type="button" onClick={() => onClose(false)}>
                {t('limits.cancel')}
              </button>
              <button
                className="btn danger"
                type="button"
                disabled={remove.isPending || sessions.length === running.length}
                onClick={() => remove.mutate({ paths: sessions.map((s) => s.path), permanent })}
              >
                <Icon name="trash3" />
                {permanent ? t('sessions.deleteNow') : t('sessions.toRecycleBin')}
              </button>
            </>
          )}
        </footer>
      </div>
    </div>
  );
}

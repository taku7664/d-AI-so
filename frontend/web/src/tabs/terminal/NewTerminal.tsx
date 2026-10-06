// 새 터미널 단계 카드 (docs/design/daiso-d.html "새 터미널"). AI → 폴더 → 대화 → 세부 설정. 지금 프로젝트가 있으면 폴더는 채워져 있다
import { useState } from 'react';
import { useAccounts } from '../../accounts';
import { Icon } from '../../icons/Icon';
import { useCurrentProject, useProjects } from '../../project';
import { t } from '../../strings';
import { ToolBadge, useTools } from '../../tools';
import { useSessions, DEFAULT_FILTER } from '../sessions/api';
import { dateTime } from '../../format';
import { useModels, useOpenExternal, useOpenRoom, type OpenRequest, type Room } from './api';

export interface Draft {
  tool: string | null;
  folder: string | null;
  resume: string | null | undefined; // undefined: 아직 안 고름, null: 새로 시작
  model: string;
  args: string;
}

export const EMPTY_DRAFT: Draft = { tool: null, folder: null, resume: undefined, model: '', args: '' };

export function NewTerminal({
  draft,
  onDraft,
  onOpened,
}: {
  draft: Draft;
  onDraft: (draft: Draft) => void;
  onOpened: (room: Room) => void;
}) {
  const tools = useTools();
  const accounts = useAccounts();
  const project = useCurrentProject();
  const projects = useProjects();
  const open = useOpenRoom();
  const external = useOpenExternal();
  const folder = draft.folder ?? (project?.exists ? project.path : null);
  const models = useModels(draft.tool);
  const sessions = useSessions({ ...DEFAULT_FILTER, tool: draft.tool, project: folder });
  const [typed, setTyped] = useState('');

  const installed = (id: string) => accounts.data?.find((a) => a.tool === id)?.installed ?? true;
  const toolOk = draft.tool !== null && installed(draft.tool);
  const ready = toolOk && folder !== null && draft.resume !== undefined;
  const request: OpenRequest | null = ready
    ? {
        tool: draft.tool!,
        folder: folder!,
        resumePath: draft.resume ?? null,
        model: draft.model || null,
        arguments: draft.args || null,
        name: null,
      }
    : null;
  const current = !draft.tool ? 1 : !folder ? 2 : draft.resume === undefined ? 3 : 4;
  const step = (n: number, done: boolean) =>
    `step ${done ? 'done' : n === current ? 'now' : n > current ? 'later' : ''}`;
  const mark = (n: number, done: boolean) => (
    <span className="mark">{done ? <Icon name="check2" /> : n === current ? '●' : ''}</span>
  );
  const recent = (sessions.data?.sessions ?? []).slice(0, 30);
  const folders = (projects.data?.projects ?? []).filter((p) => p.exists);

  return (
    <div className="nt">
      <div className="nt-steps">
        <div className={step(1, !!draft.tool)}>
          {mark(1, !!draft.tool)}
          <h4>{t('nt.tool')}</h4>
          <div className="sbody">
            <div className="tools3">
              {tools.data?.map((tool) => (
                <button
                  key={tool.id}
                  className="toolpick"
                  type="button"
                  aria-pressed={draft.tool === tool.id}
                  onClick={() => onDraft({ ...draft, tool: tool.id, resume: undefined, model: '' })}
                >
                  <ToolBadge tool={tool} />
                  <span>
                    <b>{tool.title}</b>
                    <br />
                    <small className={installed(tool.id) ? '' : 'miss'}>
                      {installed(tool.id) ? t('nt.ready') : t('account.state.notInstalled')}
                    </small>
                  </span>
                </button>
              ))}
            </div>
            {draft.tool && !installed(draft.tool) && <div className="lnote err">{t('nt.notInstalled')}</div>}
          </div>
        </div>

        {toolOk && (
          <div className={step(2, !!folder)}>
            {mark(2, !!folder)}
            <h4>
              {t('nt.folder')} {project && folder === project.path && <small>{t('nt.currentProject')}</small>}
            </h4>
            <div className="sbody">
              <select
                className="select"
                value={folder ?? ''}
                onChange={(e) => onDraft({ ...draft, folder: e.target.value || null, resume: undefined })}
              >
                <option value="">{t('nt.pickFolder')}</option>
                {folder && !folders.some((f) => f.path === folder) && <option value={folder}>{folder}</option>}
                {folders.map((f) => (
                  <option key={f.path} value={f.path}>
                    {f.name} — {f.path}
                  </option>
                ))}
              </select>
              <span className="with-btn">
                <input
                  className="input mono"
                  placeholder={t('nt.typeFolder')}
                  value={typed}
                  onChange={(e) => setTyped(e.target.value)}
                  onKeyDown={(e) =>
                    e.key === 'Enter' && typed.trim() && onDraft({ ...draft, folder: typed.trim(), resume: undefined })
                  }
                />
                <button
                  className="btn small"
                  type="button"
                  disabled={!typed.trim()}
                  onClick={() => onDraft({ ...draft, folder: typed.trim(), resume: undefined })}
                >
                  {t('nt.useFolder')}
                </button>
              </span>
            </div>
          </div>
        )}

        {toolOk && folder && (
          <div className={step(3, draft.resume !== undefined)}>
            {mark(3, draft.resume !== undefined)}
            <h4>{t('nt.conversation')}</h4>
            <div className="sbody">
              <select
                className="select"
                value={draft.resume === undefined ? '' : (draft.resume ?? '__new')}
                onChange={(e) =>
                  onDraft({
                    ...draft,
                    resume: e.target.value === '' ? undefined : e.target.value === '__new' ? null : e.target.value,
                  })
                }
              >
                <option value="">{t('nt.pickConversation')}</option>
                <option value="__new">{t('nt.newConversation')}</option>
                {recent.map((s) => (
                  <option key={s.path} value={s.path}>
                    {dateTime(s.modifiedAt)} · {s.title || t('sessions.noPrompt')}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )}

        {ready && (
          <div className={`step ${current === 4 ? 'now' : ''}`}>
            <span className="mark">
              <Icon name="sliders" />
            </span>
            <h4>
              {t('nt.details')} <small>{t('nt.optional')}</small>
            </h4>
            <div className="sbody">
              <label className="lbl-row">
                <span>{t('nt.model')}</span>
                <select
                  className="select"
                  value={draft.model}
                  onChange={(e) => onDraft({ ...draft, model: e.target.value })}
                >
                  <option value="">{t('nt.defaultModel')}</option>
                  {models.data?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="lbl-row">
                <span>{t('nt.args')}</span>
                <input
                  className="input mono"
                  placeholder={t('nt.argsHint')}
                  value={draft.args}
                  onChange={(e) => onDraft({ ...draft, args: e.target.value })}
                />
              </label>
            </div>
          </div>
        )}
      </div>
      <div className="nt-foot">
        <span className={ready ? 'sentence' : 'left-hint'}>
          {ready
            ? t(draft.resume ? 'nt.sentenceResume' : 'nt.sentenceNew', {
                tool: tools.data?.find((x) => x.id === draft.tool)?.title ?? '',
                folder: folder!.split(/[\\/]/).pop() ?? folder!,
              })
            : !draft.tool
              ? t('nt.leftTool')
              : !toolOk
                ? t('nt.notInstalled')
                : !folder
                  ? t('nt.leftFolder')
                  : t('nt.leftConversation')}
        </span>
        <div className="go">
          {(open.isError || external.isError) && (
            <span className="lnote err">{(open.error ?? external.error)?.message}</span>
          )}
          <button
            className="btn"
            type="button"
            disabled={!request || external.isPending}
            onClick={() => request && external.mutate(request)}
          >
            <Icon name="box-arrow-up-right" />
            {t('nt.external')}
          </button>
          <button
            className="btn primary"
            type="button"
            disabled={!request || open.isPending}
            onClick={() => request && open.mutate(request, { onSuccess: onOpened })}
          >
            <Icon name={draft.resume ? 'arrow-repeat' : 'play-fill'} />
            {draft.resume ? t('nt.resume') : t('nt.start')}
          </button>
        </div>
      </div>
    </div>
  );
}

// 위 줄 왼쪽 프로젝트 선택기 (docs/design/README.md). 열기: 누르기 또는 Ctrl+P. 방향키로 고르고 Enter, Esc 로 닫는다
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../icons/Icon';
import { useCurrentProject, useProjects, useSetCurrentProject, type Project } from '../project';
import { t } from '../strings';

/** 오늘이면 시각, 아니면 월-일 */
function when(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = new Date(iso);
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return date.toDateString() === now.toDateString()
    ? `${pad(date.getHours())}:${pad(date.getMinutes())}`
    : `${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function ProjectPicker({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const projects = useProjects();
  const current = useCurrentProject();
  const setCurrent = useSetCurrentProject();
  const box = useRef<HTMLSpanElement>(null);
  const list: (Project | null)[] = [null, ...(projects.data?.projects ?? [])];
  const [active, setActive] = useState(0);

  // 열릴 때 지금 프로젝트에 맞춰 둔다. 바깥을 누르면 닫는다
  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) onOpenChange(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [open, onOpenChange]);

  const choose = (project: Project | null) => {
    onOpenChange(false);
    if ((project?.path ?? null) !== (projects.data?.current ?? null)) setCurrent.mutate(project?.path ?? null);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (!open) return;
    if (event.key === 'Escape') onOpenChange(false);
    else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      setActive((index) => (index + (event.key === 'ArrowDown' ? 1 : -1) + list.length) % list.length);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      choose(list[active] ?? null);
    }
  };

  const toggle = () => {
    if (!open)
      setActive(
        Math.max(
          0,
          list.findIndex((item) => (item?.path ?? null) === (current?.path ?? null)),
        ),
      );
    onOpenChange(!open);
  };

  return (
    <span className="anchor" ref={box} onKeyDown={onKeyDown}>
      <button
        className="projbtn"
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open}
        title={t('top.project.hint')}
        onClick={toggle}
        autoFocus={open}
      >
        <Icon name={current ? 'folder' : 'grid'} />
        <span className="ell">{current ? current.name : t('top.project.all')}</span>
        <Icon name="chevron-down" />
      </button>
      {open && (
        <div className="pop proj-pop" role="listbox" aria-label={t('top.project.hint')}>
          {projects.isPending && <div className="pop-note">{t('top.project.loading')}</div>}
          {projects.isError && <div className="pop-note">{t('top.project.failed')}</div>}
          {list.map((project, index) => (
            <div key={project?.path ?? ''}>
              <button
                className={`menu-item ${index === active ? 'active' : ''}`}
                type="button"
                role="option"
                aria-selected={index === active}
                aria-current={(project?.path ?? null) === (current?.path ?? null)}
                title={project?.path}
                onMouseEnter={() => setActive(index)}
                onClick={() => choose(project)}
              >
                <Icon name={project ? 'folder' : 'grid'} />
                <b className="ell">{project ? project.name : t('top.project.all')}</b>
                <small>
                  {project
                    ? project.exists
                      ? when(project.lastActivity) || t('top.project.sessions', { count: project.sessionCount })
                      : t('top.project.missing')
                    : ''}
                </small>
              </button>
              {index === 0 && <div className="menu-sep" />}
            </div>
          ))}
          {projects.isSuccess && list.length === 1 && <div className="pop-note">{t('top.project.none')}</div>}
        </div>
      )}
    </span>
  );
}

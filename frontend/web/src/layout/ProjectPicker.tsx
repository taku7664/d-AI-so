// 위 줄 왼쪽 프로젝트 선택기 (docs/design/README.md). 열기: 누르기 또는 Ctrl+P. 방향키로 고르고 Enter, Esc 로 닫는다
import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react';
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
  const button = useRef<HTMLButtonElement>(null);
  const list: (Project | null)[] = [null, ...(projects.data?.projects ?? [])];
  // 방향키로 옮긴 자리. null 이면 지금 프로젝트 자리다. 열 때마다 지금 프로젝트에서 시작한다
  const [moved, setMoved] = useState<number | null>(null);
  const active =
    moved ??
    Math.max(
      0,
      list.findIndex((item) => (item?.path ?? null) === (current?.path ?? null)),
    );

  const close = useCallback(() => {
    setMoved(null);
    onOpenChange(false);
  }, [onOpenChange]);

  // Ctrl+P 로 열면 초점이 밖에 있다. 방향키가 닿게 단추로 옮긴다
  useEffect(() => {
    if (open) button.current?.focus();
  }, [open]);

  // 바깥을 누르면 닫는다
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) close();
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open, close]);

  const choose = (project: Project | null) => {
    close();
    if ((project?.path ?? null) !== (projects.data?.current ?? null)) setCurrent.mutate(project?.path ?? null);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (!open) return;
    if (event.key === 'Escape') close();
    else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      setMoved((active + (event.key === 'ArrowDown' ? 1 : -1) + list.length) % list.length);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      choose(list[active] ?? null);
    }
  };

  const toggle = () => (open ? close() : onOpenChange(true));

  return (
    <span className="anchor" ref={box} onKeyDown={onKeyDown}>
      <button
        className="projbtn"
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open}
        title={t('top.project.hint')}
        onClick={toggle}
        ref={button}
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
                onMouseEnter={() => setMoved(index)}
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

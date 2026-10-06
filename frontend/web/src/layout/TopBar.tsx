// 위 줄: 프로젝트 선택기 · 경로 · 종 · 계정 · 톱니 (docs/design/README.md)
// 종(Stage 6)은 자리만 둔다
import { AccountButton } from '../accounts';
import { Icon } from '../icons/Icon';
import { useCurrentProject } from '../project';
import { navigate } from '../router';
import { t } from '../strings';
import { ProjectPicker } from './ProjectPicker';

export function TopBar({
  pickerOpen,
  onPickerOpenChange,
  settingsOpen,
}: {
  pickerOpen: boolean;
  onPickerOpenChange: (open: boolean) => void;
  settingsOpen: boolean;
}) {
  const project = useCurrentProject();
  return (
    <div className="topbar">
      <ProjectPicker open={pickerOpen} onOpenChange={onPickerOpenChange} />
      {project && (
        <span className="path mono ell" title={project.path}>
          {project.path}
        </span>
      )}
      <span className="grow" />
      <button
        className="icon-btn"
        type="button"
        disabled
        title={`${t('top.bell')} · ${t('top.soon', { stage: 'Stage 6' })}`}
      >
        <Icon name="bell" label={t('top.bell')} />
      </button>
      <AccountButton />
      <button
        className="icon-btn"
        type="button"
        aria-pressed={settingsOpen}
        title={t('top.settings')}
        onClick={() => (settingsOpen ? history.back() : navigate('settings'))}
      >
        <Icon name="gear" label={t('top.settings')} />
      </button>
    </div>
  );
}

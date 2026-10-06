// 위 줄: 프로젝트 선택기 · 경로 · 종 · 계정 · 톱니 (docs/design/README.md)
import { AccountButton } from '../accounts';
import { Bell } from './Bell';
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
      <Bell />
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

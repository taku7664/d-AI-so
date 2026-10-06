// 설정은 메뉴에 없다. 위 줄 톱니로 연다 (docs/DECISIONS.md "탭은 여섯 개")
import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function SettingsView() {
  return <Placeholder id="settings" stage="Stage 7" />;
}

export const tab: TabModule = {
  id: 'settings',
  title: t('tab.settings'),
  icon: 'gear',
  order: 70,
  inMenu: false,
  View: SettingsView,
};

import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function DashboardView() {
  return <Placeholder id="dashboard" stage="Stage 5" />;
}

export const tab: TabModule = {
  id: 'dashboard',
  title: t('tab.dashboard'),
  icon: 'house-door',
  order: 10,
  shortcut: 'Ctrl+1',
  inMenu: true,
  View: DashboardView,
};

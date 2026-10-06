import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function UsageView() {
  return <Placeholder id="usage" stage="Stage 4" />;
}

export const tab: TabModule = {
  id: 'usage',
  title: t('tab.usage'),
  icon: 'bar-chart-line',
  order: 60,
  shortcut: 'Ctrl+6',
  inMenu: true,
  View: UsageView,
};

import { t } from '../../strings';
import type { TabModule } from '../types';
import { UsageView } from './UsageView';

export const tab: TabModule = {
  id: 'usage',
  title: t('tab.usage'),
  icon: 'bar-chart-line',
  order: 60,
  shortcut: 'Ctrl+6',
  inMenu: true,
  View: UsageView,
};

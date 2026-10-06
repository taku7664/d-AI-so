import { t } from '../../strings';
import type { TabModule } from '../types';
import { DashboardView } from './DashboardView';

export const tab: TabModule = {
  id: 'dashboard',
  title: t('tab.dashboard'),
  icon: 'house-door',
  order: 10,
  shortcut: 'Ctrl+1',
  inMenu: true,
  pageClass: 'sum-page',
  View: DashboardView,
};

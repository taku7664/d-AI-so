import { t } from '../../strings';
import type { TabModule } from '../types';
import { TerminalView } from './TerminalView';

export const tab: TabModule = {
  id: 'terminal',
  title: t('tab.terminal'),
  icon: 'terminal',
  order: 20,
  shortcut: 'Ctrl+2',
  inMenu: true,
  pageClass: 'fixed term-page',
  View: TerminalView,
};

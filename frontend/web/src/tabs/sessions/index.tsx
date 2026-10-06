import { t } from '../../strings';
import type { TabModule } from '../types';
import { SessionsView } from './SessionsView';

export const tab: TabModule = {
  id: 'sessions',
  title: t('tab.sessions'),
  icon: 'chat-left-text',
  order: 30,
  shortcut: 'Ctrl+3',
  inMenu: true,
  pageClass: 'fixed sess-page',
  View: SessionsView,
};

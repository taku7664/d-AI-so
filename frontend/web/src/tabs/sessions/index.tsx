import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function SessionsView() {
  return <Placeholder id="sessions" stage="Stage 5" />;
}

export const tab: TabModule = {
  id: 'sessions',
  title: t('tab.sessions'),
  icon: 'chat-left-text',
  order: 30,
  shortcut: 'Ctrl+3',
  inMenu: true,
  View: SessionsView,
};

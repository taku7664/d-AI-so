import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function TerminalView() {
  return <Placeholder id="terminal" stage="Stage 6" />;
}

export const tab: TabModule = {
  id: 'terminal',
  title: t('tab.terminal'),
  icon: 'terminal',
  order: 20,
  shortcut: 'Ctrl+2',
  inMenu: true,
  View: TerminalView,
};

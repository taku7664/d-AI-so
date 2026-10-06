import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function RulesView() {
  return <Placeholder id="rules" stage="Stage 7" />;
}

export const tab: TabModule = {
  id: 'rules',
  title: t('tab.rules'),
  icon: 'journal-check',
  order: 40,
  shortcut: 'Ctrl+4',
  inMenu: true,
  View: RulesView,
};

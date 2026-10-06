import { Placeholder } from '../../layout/Placeholder';
import { t } from '../../strings';
import type { TabModule } from '../types';

function PromptsView() {
  return <Placeholder id="prompts" stage="Stage 7" />;
}

export const tab: TabModule = {
  id: 'prompts',
  title: t('tab.prompts'),
  icon: 'lightning-charge',
  order: 50,
  shortcut: 'Ctrl+5',
  inMenu: true,
  View: PromptsView,
};

// 기본 탭 목록. 탭을 더하려면 tabs/{id}/index.tsx 를 만들고 여기 한 줄을 더한다 (frontend/web/src/tabs/README.md)
import { tab as dashboard } from './dashboard';
import { tab as prompts } from './prompts';
import { tab as rules } from './rules';
import { tab as sessions } from './sessions';
import { tab as settings } from './settings';
import { tab as terminal } from './terminal';
import type { TabModule } from './types';
import { tab as usage } from './usage';

export const TABS: readonly TabModule[] = [dashboard, terminal, sessions, rules, prompts, usage, settings].sort(
  (a, b) => a.order - b.order,
);

export const MENU_TABS = TABS.filter((tab) => tab.inMenu);

/** 주소 첫 칸으로 탭을 찾는다. 빈 칸이나 모르는 이름이면 요약이다 */
export function tabFor(pathId: string): TabModule {
  return TABS.find((tab) => tab.id === pathId) ?? dashboard;
}

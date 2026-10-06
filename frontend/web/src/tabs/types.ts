// 탭 약속 (frontend/web/src/tabs/README.md). id 는 서버 경로 /api/{id} 와 같고, 정한 뒤에는 바꾸지 않는다
import type { ComponentType } from 'react';

export interface TabModule {
  /** 소문자와 `-` 만. 서버 쪽 ITabEndpoints.Id 와 같다 */
  id: string;
  title: string;
  /** Bootstrap Icons 이름 */
  icon: string;
  /** 메뉴 순서. 10 단위 */
  order: number;
  /** `Ctrl+1`~`Ctrl+6`. 메뉴에 없는 탭은 비운다 */
  shortcut?: string;
  /** 왼쪽 메뉴에 띄울지. 설정만 false 이고 위 줄 톱니로 연다 */
  inMenu: boolean;
  /** 본문 바탕 모양. 비우면 위에서 아래로 흐르는 페이지. 목록·상세처럼 칸을 나누는 탭은 'fixed sess-page' 같은 클래스를 준다 */
  pageClass?: string;
  View: ComponentType;
}

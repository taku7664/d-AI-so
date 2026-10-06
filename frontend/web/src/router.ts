// 주소로 탭을 가른다: / 는 요약, /{탭 id} 는 그 탭. 크롬 탭에서 AI 가 주소로 바로 열 수 있게 하려는 것이다
// 탭 주소로 새로 열어도 Host 가 index.html 을 낸다 (backend/src/Daiso.Host/DaisoHost.cs)
import { useSyncExternalStore } from 'react';

const CHANGE = 'daiso:navigate';

function subscribe(notify: () => void): () => void {
  window.addEventListener('popstate', notify);
  window.addEventListener(CHANGE, notify);
  return () => {
    window.removeEventListener('popstate', notify);
    window.removeEventListener(CHANGE, notify);
  };
}

/** 주소의 첫 칸. `/` 면 빈 문자열. */
export function usePathId(): string {
  return useSyncExternalStore(subscribe, () => location.pathname.split('/')[1] ?? '');
}

export function navigate(id: string): void {
  const path = id ? `/${id}` : '/';
  if (location.pathname === path) return;
  history.pushState(null, '', path);
  window.dispatchEvent(new Event(CHANGE));
}

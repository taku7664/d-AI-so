// 공용 WebSocket /ws 의 알림을 받아 캐시를 무효화한다 (frontend/web/src/tabs/README.md "서버 알림을 받는 법")
// 알림은 { tab, kind } 뿐이다. 값은 믿지 않고 그 탭(또는 공용 경로 이름)으로 시작하는 쿼리를 다시 받는다
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

const ALSO_DASHBOARD = new Set(['accounts', 'sessions', 'index', 'projects']);

interface ServerNotification {
  tab: string;
  kind: string;
}

function parse(data: unknown): ServerNotification | null {
  if (typeof data !== 'string') return null;
  try {
    const value: unknown = JSON.parse(data);
    if (value && typeof value === 'object' && 'tab' in value && typeof value.tab === 'string') {
      return { tab: value.tab, kind: 'kind' in value && typeof value.kind === 'string' ? value.kind : '' };
    }
  } catch {
    // 알 수 없는 메시지는 버린다
  }
  return null;
}

/** /ws 에 붙어 있는다. 끊기면 0.5초부터 10초까지 늘려 가며 다시 잇는다. 지금 붙어 있는지 돌려준다. */
export function useServerEvents(): boolean {
  const queryClient = useQueryClient();
  const [connected, setConnected] = useState(true);

  useEffect(() => {
    let socket: WebSocket | null = null;
    let retry = 0;
    let timer: number | undefined;
    let stopped = false;

    const open = (): void => {
      socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`);
      socket.onopen = () => {
        // 처음 붙을 때는 다시 받을 것이 없다. 끊겼다가 다시 붙었으면 그 사이 알림을 놓쳤을 수 있다
        if (retry > 0) void queryClient.invalidateQueries();
        retry = 0;
        setConnected(true);
      };
      socket.onmessage = (event) => {
        const notification = parse(event.data);
        if (!notification) return;
        void queryClient.invalidateQueries({ queryKey: [notification.tab] });
        // 요약은 여러 곳의 값을 모아 보여 준다. 그 값이 바뀌면 요약도 다시 받는다
        if (ALSO_DASHBOARD.has(notification.tab)) void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      };
      socket.onclose = () => {
        if (stopped) return;
        setConnected(false);
        timer = window.setTimeout(open, Math.min(10_000, 500 * 2 ** retry++));
      };
    };

    open();
    return () => {
      stopped = true;
      window.clearTimeout(timer);
      socket?.close();
    };
  }, [queryClient]);

  return connected;
}

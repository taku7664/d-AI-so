// 완료 알림 (옛 앱 e682f15 DoneNotifier). 방에 안 본 답이 새로 생기면(종 숫자가 느는 그 순간) 윈도우 알림을 한 번 띄운다
//   - 한 번 끝날 때 한 번만. 그 방을 보고 나서(안 본 표시가 꺼짐) 다시 켜져야 다음 알림이다
//   - 보고 있는 방(창이 앞에 있고 그 방이 화면에 있음)은 알리지 않는다
//   - 앱을 막 열었을 때 이미 안 본 방은 알리지 않는다
//   - Electron 은 메인의 Notification, 브라우저 탭은 웹 알림(처음 한 번 권한을 묻는다)
// 누르면 창을 앞으로 꺼내고 그 방으로 간다(지금 프로젝트 밖이면 프로젝트를 바꾼다)
import { useEffect, useRef } from 'react';
import { desktop } from '../desktop';
import { t } from '../strings';
import { viewingRoom, useRooms, type Room } from '../tabs/terminal/api';
import { useTools } from '../tools';
import { useGoToRoom } from './goToRoom';

export function useDoneNotices() {
  const rooms = useRooms();
  const tools = useTools();
  const goToRoom = useGoToRoom();
  const go = useRef(goToRoom);
  useEffect(() => {
    go.current = goToRoom;
  });
  const before = useRef<Map<string, boolean> | null>(null);

  // Electron: 알림을 누르면 메인이 창을 꺼내고 방 id 를 보낸다
  useEffect(() => desktop?.onOpenRoom?.((id) => go.current(id)), []);

  useEffect(() => {
    if (!rooms.data) return;
    const last = before.current;
    before.current = new Map(rooms.data.map((room) => [room.id, room.unseen]));
    if (!last) return;
    for (const room of rooms.data) {
      if (!room.unseen || last.get(room.id)) continue;
      if (document.hasFocus() && viewingRoom() === room.id) continue;
      const tool = tools.data?.find((x) => x.id === room.tool)?.title ?? room.tool;
      show(room, t('notice.doneTitle', { tool }), `${room.project.split(/[\\/]/).pop()} · ${room.name}`, (id) =>
        go.current(id),
      );
    }
  }, [rooms.data, tools.data]);
}

function show(room: Room, title: string, body: string, open: (id: string) => void) {
  if (desktop?.notify) {
    desktop.notify({ room: room.id, title, body });
    return;
  }
  if (typeof Notification === 'undefined') return;
  const pop = () => {
    const notice = new Notification(title, { body, tag: room.id });
    notice.onclick = () => {
      window.focus();
      open(room.id);
      notice.close();
    };
  };
  if (Notification.permission === 'granted') pop();
  else if (Notification.permission === 'default')
    void Notification.requestPermission().then((answer) => answer === 'granted' && pop());
}

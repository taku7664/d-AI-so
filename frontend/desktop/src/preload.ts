// 화면에 여는 Electron 전용 다리. 화면은 window.daisoDesktop 이 있는지만 보고 Electron 전용 기능을 켠다 (docs/DECISIONS.md "좁은 다리 하나")
// 쓰는 기능이 생길 때 하나씩 더한다
import { contextBridge, ipcRenderer, webUtils, type IpcRendererEvent } from 'electron';

contextBridge.exposeInMainWorld(
  'daisoDesktop',
  Object.freeze({
    // 끌어 놓았거나 고른 파일의 전체 경로. 브라우저는 경로를 주지 않는다. 클립보드에서 온 그림처럼 파일이 없으면 빈 글자
    pathForFile: (file: File): string => webUtils.getPathForFile(file),
    // 윈도우 알림. 누르면 창을 꺼내고 onOpenRoom 으로 그 방 id 가 온다
    notify: (notice: { room: string; title: string; body: string }): void =>
      ipcRenderer.send('daiso:notify', { room: notice.room, title: notice.title, body: notice.body }),
    onOpenRoom: (listener: (room: string) => void): (() => void) => {
      const handler = (_event: IpcRendererEvent, room: unknown) => {
        if (typeof room === 'string') listener(room);
      };
      ipcRenderer.on('daiso:open-room', handler);
      return () => ipcRenderer.off('daiso:open-room', handler);
    },
  }),
);

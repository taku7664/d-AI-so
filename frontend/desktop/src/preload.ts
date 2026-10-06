// 화면에 여는 Electron 전용 다리. 화면은 window.daisoDesktop 이 있는지만 보고 Electron 전용 기능을 켠다 (docs/DECISIONS.md "좁은 다리 하나")
// 지금은 비어 있다. 쓰는 기능이 생길 때(끌어 놓기 경로, 알림 등) 하나씩 더한다
import { contextBridge } from 'electron';

contextBridge.exposeInMainWorld('daisoDesktop', Object.freeze({}));

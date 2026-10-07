// Electron 메인. Daiso.Host 를 띄워 알려 준 주소를 창에 열고, 트레이에 머문다 (docs/ARCHITECTURE.md "프로세스 구성")
// 앱을 끝내면 Host 는 부모(이 프로세스)가 끝난 것을 보고 스스로 끝난다 (Daiso.Host 의 ParentWatcher)
import { app, BrowserWindow, dialog, ipcMain, Menu, nativeTheme, Notification, shell, Tray } from 'electron';
import path from 'node:path';
import { startHost, type RunningHost } from './host';

let host: RunningHost | null = null;
let win: BrowserWindow | null = null;
let tray: Tray | null = null;
let quitting = false;

const ICON = path.join(__dirname, '..', 'assets', 'daiso.ico');

function hostExe(): string {
  if (app.isPackaged) return path.join(process.resourcesPath, 'host', 'Daiso.Host.exe');
  // 개발 중에는 tools/run-app.ps1 이 빌드한 바로 그 Host 경로를 넘긴다. 경로를 여기서 짐작하지 않는다
  const exe = process.env.DAISO_HOST_EXE;
  if (!exe) throw new Error('DAISO_HOST_EXE 가 없습니다. 저장소 루트의 tools/run-app.ps1 로 띄우세요');
  return exe;
}

function showWindow(): void {
  // 창이 어떤 까닭으로든 없어졌으면(페이지가 window.close() 를 부르는 등) 새로 만든다. 트레이만 남고 창을 못 여는 일이 없게
  if (!win || win.isDestroyed()) {
    if (host) createWindow(host);
    return;
  }
  if (win.isMinimized()) win.restore();
  win.show();
  win.focus();
}

function quit(): void {
  quitting = true;
  app.quit();
}

// http·https 만 기본 브라우저로 넘긴다. file: 같은 것은 열지 않는다
function openOutside(url: string): void {
  if (/^https?:\/\//i.test(url)) void shell.openExternal(url);
}

function createWindow(h: RunningHost): void {
  win = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 360,
    minHeight: 480,
    show: false,
    icon: ICON,
    title: 'DAIso',
    // 화면이 뜨기 전 잠깐 보이는 바탕. 시안의 --ground 와 같다
    backgroundColor: nativeTheme.shouldUseDarkColors ? '#121411' : '#eceee7',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  win.once('ready-to-show', () => win?.show());
  // X 는 트레이로 숨긴다. 끝내기는 트레이 메뉴에서
  win.on('close', (e) => {
    if (quitting) return;
    e.preventDefault();
    win?.hide();
  });
  win.on('closed', () => {
    win = null;
  });

  // 창은 Host 출처 안에서만 움직인다. 바깥 링크는 기본 브라우저로 넘긴다
  win.webContents.on('will-navigate', (e, url) => {
    if (new URL(url).origin === h.origin) return;
    e.preventDefault();
    openOutside(url);
  });
  win.webContents.setWindowOpenHandler(({ url }) => {
    openOutside(url);
    return { action: 'deny' };
  });

  void win.loadURL(h.openUrl);
}

// 완료 알림 (옛 앱 e682f15 DoneNotifier). 언제 띄울지는 화면이 정한다(안 본 답이 새로 생긴 방). 누르면 창을 꺼내 그 방을 연다
// 알림 객체를 잡아 두지 않으면 GC 가 거둬 누르기가 오지 않는다. 닫히거나 눌릴 때까지 들고 있는다
const notices = new Set<Notification>();

function text(value: unknown, max: number): string | null {
  return typeof value === 'string' && value.length > 0 && value.length <= max ? value : null;
}

function listenNotices(): void {
  ipcMain.on('daiso:notify', (event, payload: { room?: unknown; title?: unknown; body?: unknown }) => {
    // 우리 창이 보낸 것만 받는다
    if (!win || win.isDestroyed() || event.sender !== win.webContents) return;
    const room = text(payload?.room, 64);
    const title = text(payload?.title, 200);
    const body = text(payload?.body, 400) ?? '';
    if (!room || !title || !Notification.isSupported()) return;

    const notice = new Notification({ title, body, icon: ICON });
    notices.add(notice);
    const forget = () => notices.delete(notice);
    notice.on('click', () => {
      forget();
      const before = win;
      showWindow();
      // 창이 없어져 새로 만들었으면 그 창은 아직 뜨는 중이라 방을 열라고 할 곳이 없다. 창만 꺼낸다
      if (win && win === before && !win.isDestroyed()) win.webContents.send('daiso:open-room', room);
    });
    notice.on('close', forget);
    notice.on('failed', forget);
    notice.show();
  });
}

function createTray(h: RunningHost): void {
  tray = new Tray(ICON);
  tray.setToolTip('DAIso');
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: '열기', click: showWindow },
      // 기본 브라우저에서도 같은 화면을 연다. 첫 접속 주소라 토큰이 붙는다 (docs/SECURITY.md 3)
      { label: '브라우저로 열기', click: () => void shell.openExternal(h.openUrl) },
      { type: 'separator' },
      { label: '끝내기', click: quit },
    ]),
  );
  tray.on('click', showWindow);
}

async function main(): Promise<void> {
  const h = await startHost(hostExe());
  host = h;
  h.process.once('exit', (code) => {
    if (quitting) return;
    dialog.showErrorBox('DAIso', `서버가 예기치 않게 끝났습니다 (코드 ${code}). 앱을 닫습니다.`);
    quit();
  });
  createTray(h);
  listenNotices();
  createWindow(h);
}

// --quit: 떠 있는 앱을 트레이의 "끝내기"처럼 끈다(tools/stop-app.ps1). 프로세스를 강제로 죽이면 트레이 아이콘이 지워지지 않고 남는다
const quitRequest = process.argv.includes('--quit');

if (!app.requestSingleInstanceLock() || quitRequest) {
  // 이미 떠 있는 앱이 second-instance 를 받아 창을 앞으로 꺼내거나 끝낸다. 떠 있는 앱이 없는데 --quit 이면 그냥 끝난다
  app.quit();
} else {
  app.on('second-instance', (_event, argv) => (argv.includes('--quit') ? quit() : showWindow()));
  app.on('before-quit', () => {
    quitting = true;
  });
  // 끝내기의 마지막 단계. 창은 이미 닫혔다. 트레이를 지우고 Chromium 의 종료 정리를 건너뛰어 스스로 끝낸다.
  // Electron 44 의 Chromium 은 종료 정리에서 GPU 프로세스를 먼저 치운 뒤 브라우저 쪽 GPU 컨텍스트를 치우며 GPU 에 동기 호출을 한다.
  // GPU 프로세스가 그 사이 먼저 끝나 버리면 답이 오지 않아 메인만 영영 남는다(2026-10-07 덤프로 확인. README "알려진 문제").
  // process.exit 는 app.exit 로 이어져 같은 정리를 타므로 쓸 수 없다. 종료 코드는 1 이 된다
  app.on('quit', () => {
    tray?.destroy();
    tray = null;
    process.kill(process.pid);
  });
  // 창을 숨겨도 트레이에 남는다. 창이 다 닫혀도 끝내지 않는다
  app.on('window-all-closed', () => undefined);
  // 윈도우 알림에 앱 이름이 뜨게 한다. 설치판(Stage 8)은 시작 메뉴 바로 가기에 같은 값을 넣는다
  app.setAppUserModelId('DAIso');
  app
    .whenReady()
    .then(main)
    .catch((error: unknown) => {
      dialog.showErrorBox('DAIso를 시작하지 못했습니다', error instanceof Error ? error.message : String(error));
      host?.process.kill();
      app.exit(1);
    });
}

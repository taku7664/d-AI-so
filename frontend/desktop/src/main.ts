// Electron 메인. Daiso.Host 를 띄워 알려 준 주소를 창에 열고, 트레이에 머문다 (docs/ARCHITECTURE.md "프로세스 구성")
// 앱을 끝내면 Host 는 부모(이 프로세스)가 끝난 것을 보고 스스로 끝난다 (Daiso.Host 의 ParentWatcher)
import { app, BrowserWindow, dialog, Menu, nativeTheme, shell, Tray } from 'electron';
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
  if (!win) return;
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
  createWindow(h);
}

if (!app.requestSingleInstanceLock()) {
  // 이미 떠 있는 앱이 second-instance 를 받아 창을 앞으로 꺼낸다
  app.quit();
} else {
  app.on('second-instance', showWindow);
  app.on('before-quit', () => {
    quitting = true;
  });
  // 창을 숨겨도 트레이에 남는다. 창이 다 닫혀도 끝내지 않는다
  app.on('window-all-closed', () => undefined);
  app
    .whenReady()
    .then(main)
    .catch((error: unknown) => {
      dialog.showErrorBox('DAIso를 시작하지 못했습니다', error instanceof Error ? error.message : String(error));
      host?.process.kill();
      app.exit(1);
    });
}

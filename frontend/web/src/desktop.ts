// Electron 창이 preload 로 여는 다리(window.daisoDesktop). 브라우저 탭에서는 없다 (docs/DECISIONS.md "좁은 다리 하나")
export interface Desktop {
  pathForFile?: (file: File) => string;
  notify?: (notice: { room: string; title: string; body: string }) => void;
  onOpenRoom?: (listener: (room: string) => void) => () => void;
}

export const desktop: Desktop | undefined = (globalThis as { daisoDesktop?: Desktop }).daisoDesktop;

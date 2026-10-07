// 파일·그림을 방의 CLI 에 붙인다 (docs/DECISIONS.md "파일과 그림 붙이기", 시안 docs/design/terminal-chat.html "첨부 대기")
// 옛 앱(old/src/Daiso.App/Terminal/TerminalHost.cs)에서 확인한 길을 그대로 쓴다
//   - 그림은 시스템 클립보드에 올리고 그 도구의 그림 붙이기 키를 보낸다. CLI 가 클립보드를 스스로 읽어 [Image #1] 처럼 첨부한다
//     (옛 앱은 처음에 그림을 파일로 저장해 경로를 붙였다가 이 길로 바꿨다 — 9905cd8 → 547f681·0694861).
//     CLI 가 클립보드를 읽는 데 1초 넘게 걸리므로, 서버가 입력 줄에 첨부 표시가 그려질 때까지 기다렸다가 답한다
//   - 그 밖의 파일은 전체 경로를 붙인다. 빈칸이 있으면 따옴표로 감싸고 앞뒤에 빈칸을 둔다(앞 경로에 들러붙지 않게)
//   - 사용자의 클립보드는 덮어써진다
import { api } from '../../api/client';
import { desktop } from '../../desktop';

/** Electron 창이면 파일의 전체 경로를 안다. 브라우저 탭에서는 그림만 붙일 수 있다 */
export const knowsPaths = typeof desktop?.pathForFile === 'function';

export interface Attachment {
  key: string;
  file: File;
  /** 전체 경로. 클립보드에서 온 그림이나 브라우저 탭이면 null */
  path: string | null;
  image: boolean;
  /** 그림 미리보기 주소 */
  url: string | null;
}

// 브라우저가 그릴 수 있는 그림만 그림으로 친다. svg 는 글이라 경로로 붙인다
const IMAGE = /^image\/(png|jpe?g|gif|bmp|webp)$/;
let serial = 0;

/** 받은 파일을 붙일 거리로. 경로도 모르고 그림도 아닌 것은 뺀다(refused) */
export function toAttachments(files: File[]): { taken: Attachment[]; refused: number } {
  const taken: Attachment[] = [];
  let refused = 0;
  for (const file of files) {
    const path = (knowsPaths && desktop!.pathForFile!(file)) || null;
    const image = IMAGE.test(file.type);
    if (!image && !path) {
      refused++;
      continue;
    }
    taken.push({ key: `a${++serial}`, file, path, image, url: image ? URL.createObjectURL(file) : null });
  }
  return { taken, refused };
}

export function release(list: Attachment[]) {
  for (const item of list) if (item.url) URL.revokeObjectURL(item.url);
}

/** 끌어 놓기·붙여넣기에서 파일만 꺼낸다. 글이 같이 있으면 글이 먼저다(엑셀 칸 복사처럼 그림과 글이 같이 오는 것) */
export function filesOf(data: DataTransfer | null): File[] {
  if (!data || data.types.includes('text/plain')) return [];
  return [...data.files];
}

export function quotePaths(paths: string[]): string {
  return ` ${paths.map((path) => (path.includes(' ') ? `"${path}"` : path)).join(' ')} `;
}

/** 파일이 있는 폴더를 짧게. 방 폴더 안이면 "JBroEngine\…\Localization" 처럼 방 폴더 이름부터 */
export function where(path: string, folder: string): string {
  const split = (text: string) => text.split(/[\\/]/).filter(Boolean);
  const dir = split(path).slice(0, -1);
  const base = split(folder);
  const inside = base.length > 0 && base.every((part, i) => part.toLowerCase() === dir[i]?.toLowerCase());
  const parts = inside ? [base[base.length - 1], ...dir.slice(base.length)] : dir;
  return parts.length <= 2 ? parts.join('\\') : `${parts[0]}\\…\\${parts[parts.length - 1]}`;
}

async function asPng(file: File): Promise<Blob> {
  if (file.type === 'image/png') return file;
  const bitmap = await createImageBitmap(file);
  const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0);
  bitmap.close();
  return canvas.convertToBlob({ type: 'image/png' });
}

/** 그 도구의 그림 붙이기 키를 보내고 첨부될 때까지 기다린다. CLI 가 지금 클립보드의 그림을 읽는다 */
export async function pasteImageKeys(roomId: string): Promise<boolean> {
  const { response } = await api.POST('/api/terminal/rooms/{id}/image', { params: { path: { id: roomId } } });
  return response.ok;
}

/** 그림을 하나씩 클립보드에 올려 CLI 에 첨부한다. 첨부한 개수를 돌려준다 */
export async function attachImages(roomId: string, images: File[]): Promise<number> {
  let done = 0;
  for (const image of images) {
    try {
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': await asPng(image) })]);
    } catch {
      continue;
    }
    if (!(await pasteImageKeys(roomId))) break;
    done++;
  }
  return done;
}

/** 클립보드에 그림이 있는가. 오른쪽 클릭 붙이기에서 글이 없을 때 본다 */
export async function clipboardHasImage(): Promise<boolean> {
  try {
    const items = await navigator.clipboard.read();
    return items.some((item) => item.types.some((type) => type.startsWith('image/')));
  } catch {
    return false;
  }
}

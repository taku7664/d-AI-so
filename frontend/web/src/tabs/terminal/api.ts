// 터미널 탭 서버 호출. 쿼리 키 첫 칸은 'terminal' — 알림 { tab: "terminal" } 를 받으면 다시 받는다
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Schemas } from '../../api/client';

export type Room = Schemas['RoomInfo'];
export type OpenRequest = Schemas['OpenRoomRequest'];

export function useRooms() {
  return useQuery({
    queryKey: ['terminal', 'rooms'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/terminal/rooms');
      if (error || !data) throw new Error('방 목록을 읽지 못했다');
      return data;
    },
  });
}

export function useModels(tool: string | null) {
  return useQuery({
    queryKey: ['terminal', 'models', tool],
    enabled: tool !== null,
    staleTime: 5 * 60_000,
    queryFn: async () => {
      const { data } = await api.GET('/api/terminal/models', { params: { query: { tool: tool! } } });
      return data ?? [];
    },
  });
}

function useInvalidate() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['terminal', 'rooms'] });
}

export function useOpenRoom() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (request: OpenRequest) => {
      const { data, error } = await api.POST('/api/terminal/rooms', { body: request });
      if (error || !data)
        throw new Error((error as { detail?: string; title?: string } | undefined)?.detail ?? '방을 열지 못했다');
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useCloseRoom() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => {
      await api.POST('/api/terminal/rooms/{id}/close', { params: { path: { id } } });
    },
    onSuccess: invalidate,
  });
}

export function useRenameRoom() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, name }: { id: string; name: string }) => {
      await api.PUT('/api/terminal/rooms/{id}/name', { params: { path: { id } }, body: { name } });
    },
    onSuccess: invalidate,
  });
}

/** 봤다고 알린다. 답이 끝난 방의 초록 점이 꺼진다 */
export function markSeen(id: string): void {
  void api.POST('/api/terminal/rooms/{id}/seen', { params: { path: { id } } });
}

export function useOpenExternal() {
  return useMutation({
    mutationFn: async (request: OpenRequest) => {
      const { error } = await api.POST('/api/terminal/external', { body: request });
      if (error) throw new Error('새 창을 열지 못했다');
    },
  });
}

export function openFolder(path: string): void {
  void api.POST('/api/terminal/open-folder', { body: { path } });
}

// 다른 탭(종·요약·세션)에서 "이 방 열기"를 누르면 여기 남기고 터미널 탭으로 간다
let pendingRoom: string | null = null;

export const ROOM_EVENT = 'daiso:room';

/** 터미널 탭이 이미 떠 있으면 이벤트로, 아니면 뜰 때 꺼내 간다 */
export function requestRoom(id: string): void {
  pendingRoom = id;
  window.dispatchEvent(new CustomEvent(ROOM_EVENT, { detail: id }));
}

export function peekPendingRoom(): string | null {
  return pendingRoom;
}

export function clearPendingRoom(): void {
  pendingRoom = null;
}

// 지금 화면에 보이는 방. 완료 알림은 이 방이면 띄우지 않는다(보고 있으니까)
let viewing: string | null = null;

export function setViewingRoom(id: string | null): void {
  viewing = id;
}

export function viewingRoom(): string | null {
  return viewing;
}

// 요약의 워크트리 줄 "이 폴더에서 새 터미널". 폴더를 채운 새 터미널 카드를 연다
let pendingFolder: string | null = null;

export const NEW_TERMINAL_EVENT = 'daiso:new-terminal';

export function requestNewTerminal(folder: string): void {
  pendingFolder = folder;
  window.dispatchEvent(new CustomEvent(NEW_TERMINAL_EVENT, { detail: folder }));
}

export function peekPendingFolder(): string | null {
  return pendingFolder;
}

export function clearPendingFolder(): void {
  pendingFolder = null;
}

/** 방이 이 프로젝트 것인가. 워크트리 등 묶인 폴더까지 본다 */
export function inProject(room: Room, members: readonly string[] | null): boolean {
  if (!members) return true;
  // 서버가 정한 방의 프로젝트(워크트리면 원래 저장소)도 본다. 워크트리 폴더에 세션이 아직 없어도 묶인다
  const mine = [room.folder, room.project].map((path) => path.replace(/[\\/]+$/, '').toLowerCase());
  return members.some((member) => mine.includes(member.replace(/[\\/]+$/, '').toLowerCase()));
}

/**
 * 세션을 앱 안 방으로 이어서 연다. 프로젝트 폴더가 없어진 세션은 방을 열 수 없어 바깥 창으로 연다(서버가 사용자 폴더에서 연다).
 * 열리면 그 방으로 간다
 */
export function useResumeInRoom() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (session: Schemas['SessionRow']): Promise<string | null> => {
      if (session.orphan || !session.projectPath) {
        const { error } = await api.POST('/api/sessions/resume', { body: { path: session.path } });
        if (error) throw new Error('이어서 열지 못했다');
        return null;
      }
      const { data, error } = await api.POST('/api/terminal/rooms', {
        body: {
          tool: session.tool,
          folder: session.projectPath,
          resumePath: session.path,
          model: null,
          arguments: null,
          name: session.named ? session.title : null,
        },
      });
      if (error || !data) throw new Error((error as { detail?: string } | undefined)?.detail ?? '방을 열지 못했다');
      return data.id;
    },
    onSuccess: (id) => {
      void invalidate();
      if (id) requestRoom(id);
    },
  });
}

// ── 말풍선 보기 ──

export type ChatItem = Schemas['ChatItem'];
export type ChatData = Schemas['ChatResponse'];
export type CommandGroup = Schemas['CommandGroup'];

/**
 * 방의 말풍선. 서버는 마지막으로 받은 칸 번호 뒤만 준다. 받은 것은 캐시에 이어 붙인다.
 * 세션 기록이 바뀌면 서버 알림 { tab: "terminal" } 이 와서 다시 받는다
 */
export function useRoomChat(roomId: string) {
  const queryClient = useQueryClient();
  const key = ['terminal', 'chat', roomId];
  return useQuery({
    queryKey: key,
    queryFn: async (): Promise<ChatData> => {
      const previous = queryClient.getQueryData<ChatData>(key);
      const after = previous?.last ?? 0;
      const { data, error } = await api.GET('/api/terminal/rooms/{id}/chat', {
        params: { path: { id: roomId }, query: { after } },
      });
      if (error || !data) throw new Error('말풍선을 읽지 못했다');
      if (!previous || after === 0) return data;
      const seen = new Set(previous.items.map((item) => item.seq));
      return { ...data, items: [...previous.items, ...data.items.filter((item) => !seen.has(item.seq))] };
    },
  });
}

/** 말풍선 입력칸의 글을 방 CLI 의 입력 줄에 넣고 Enter 를 친다 */
export function useSendToRoom() {
  return useMutation({
    mutationFn: async ({ id, text }: { id: string; text: string }) => {
      const { error } = await api.POST('/api/terminal/rooms/{id}/send', { params: { path: { id } }, body: { text } });
      if (error) throw new Error('보내지 못했다');
    },
  });
}

/** / 목록 */
export function useCommands(tool: string, folder: string) {
  return useQuery({
    queryKey: ['terminal', 'commands', tool, folder],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/terminal/commands', { params: { query: { tool, folder } } });
      if (error || !data) throw new Error('명령 목록을 읽지 못했다');
      return data;
    },
    staleTime: 60_000,
  });
}

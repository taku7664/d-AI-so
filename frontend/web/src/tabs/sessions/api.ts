// 세션 탭 서버 호출. 쿼리 키 첫 칸은 'sessions' — 알림 { tab: "sessions" } 를 받으면 다시 받는다
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Schemas } from '../../api/client';

export type Session = Schemas['SessionRow'];

export interface ListFilter {
  tool: string | null;
  project: string | null;
  days: number;
  minMegabytes: number;
  orphans: boolean;
  archived: boolean;
}

export const DEFAULT_FILTER: Omit<ListFilter, 'project'> = {
  tool: null,
  days: 0,
  minMegabytes: 0,
  orphans: false,
  archived: true,
};

export function useSessions(filter: ListFilter) {
  return useQuery({
    queryKey: ['sessions', 'list', filter],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/sessions', {
        params: {
          query: {
            tool: filter.tool ?? undefined,
            project: filter.project ?? undefined,
            days: filter.days || undefined,
            minMegabytes: filter.minMegabytes || undefined,
            orphans: filter.orphans || undefined,
            archived: filter.archived ? undefined : false,
          },
        },
      });
      if (error || !data) throw new Error('세션 목록을 읽지 못했다');
      return data;
    },
  });
}

export function useSearch(query: string) {
  return useQuery({
    queryKey: ['sessions', 'search', query],
    enabled: query.length >= 2,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/sessions/search', { params: { query: { q: query } } });
      if (error || !data) throw new Error('검색하지 못했다');
      return data;
    },
  });
}

export function useMessages(path: string | null, tools: boolean) {
  return useQuery({
    queryKey: ['sessions', 'messages', path, tools],
    enabled: path !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/sessions/messages', { params: { query: { path: path!, tools } } });
      if (error || !data) throw new Error((error as { title?: string } | undefined)?.title ?? '대화를 읽지 못했다');
      return data;
    },
  });
}

function useInvalidate() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['sessions'] });
}

export function useRename() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ path, name }: { path: string; name: string | null }) => {
      const { error } = await api.PUT('/api/sessions/name', { body: { path, name } });
      if (error) throw new Error('이름을 붙이지 못했다');
    },
    onSuccess: invalidate,
  });
}

export function useDelete() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ paths, permanent }: { paths: string[]; permanent: boolean }) => {
      const { data, error } = await api.POST('/api/sessions/delete', { body: { paths, permanent } });
      if (error || !data) throw new Error('지우지 못했다');
      return data;
    },
    onSuccess: invalidate,
  });
}

export function useResume() {
  return useMutation({
    mutationFn: async (path: string) => {
      const { error } = await api.POST('/api/sessions/resume', { body: { path } });
      if (error) throw new Error((error as { title?: string }).title ?? '이어서 열지 못했다');
    },
  });
}

export function useSetCleanup() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (rule: Schemas['CleanupRule']) => {
      const { error } = await api.PUT('/api/sessions/cleanup', { body: rule });
      if (error) throw new Error('기준을 저장하지 못했다');
    },
    onSuccess: invalidate,
  });
}

/** 내려받기. 브라우저(Electron 포함)가 저장 위치를 묻는다 */
export function exportUrl(path: string): string {
  return `/api/sessions/export?path=${encodeURIComponent(path)}`;
}

// 다른 탭(요약)에서 "이 세션 보기"를 누르면 여기 남기고 세션 탭으로 간다. 세션 탭이 뜰 때 한 번 꺼내 연다
let pendingOpen: string | null = null;

export function requestOpen(path: string): void {
  pendingOpen = path;
}

/** 남긴 세션. 화면 상태의 첫 값으로 읽기만 한다(개발 모드는 첫 값 함수를 두 번 부른다) */
export function peekPendingOpen(): string | null {
  return pendingOpen;
}

/** 다 읽었으면 지운다. 다음에 세션 탭을 열 때 또 열리지 않게 */
export function clearPendingOpen(): void {
  pendingOpen = null;
}

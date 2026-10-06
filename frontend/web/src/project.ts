// 지금 프로젝트. 위 줄에서 고르면 모든 탭이 이 값을 기준으로 삼는다 (docs/DECISIONS.md "지금 프로젝트가 앱 전체의 기준")
// 값의 주인은 서버 설정이다. 화면은 /api/projects 를 캐시로만 든다
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Schemas } from './api/client';

export type Project = Schemas['ProjectItem'];

/** 쿼리 키 첫 칸은 서버 알림의 tab 이름과 같다. 알림 { tab: "projects" } 를 받으면 다시 받는다 */
const KEY = ['projects'] as const;

export function useProjects() {
  return useQuery({
    queryKey: KEY,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/projects');
      if (error || !data) throw new Error('프로젝트 목록을 읽지 못했다');
      return data;
    },
  });
}

/** 지금 프로젝트. "모든 프로젝트"면 null */
export function useCurrentProject(): Project | null {
  const { data } = useProjects();
  if (!data?.current) return null;
  return data.projects.find((project) => project.path === data.current) ?? null;
}

export function useSetCurrentProject() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (path: string | null) => {
      const { data, error } = await api.PUT('/api/projects/current', { body: { path } });
      if (error || !data) throw new Error('지금 프로젝트를 바꾸지 못했다');
      return data;
    },
    // 탭마다 프로젝트에 따라 다른 것을 보여 주므로 전부 다시 받는다
    onSuccess: (data) => {
      queryClient.setQueryData(KEY, data);
      void queryClient.invalidateQueries();
    },
  });
}

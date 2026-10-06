// 인덱스 갱신 상태(/api/index). 서버가 뜰 때 한 번 갱신하고, 끝나면 알림으로 다시 받는다
import { useMutation, useQuery } from '@tanstack/react-query';
import { api } from './api/client';

export function useIndexStatus() {
  return useQuery({
    queryKey: ['index'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/index');
      if (error || !data) throw new Error('인덱스 상태를 읽지 못했다');
      return data;
    },
  });
}

/** 바뀐 세션만 이어 읽는다. 끝나면 서버가 알림을 보내 화면이 다시 받는다 */
export function useRefreshIndex() {
  return useMutation({
    mutationFn: async () => {
      await api.POST('/api/index/refresh');
    },
  });
}

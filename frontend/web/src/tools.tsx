// 도구 목록(/api/tools)과 도구 표시. 플러그인으로 더한 도구도 같은 모양으로 그린다
import { useQuery } from '@tanstack/react-query';
import { api, type Schemas } from './api/client';

export type Tool = Schemas['ToolItem'];

export function useTools() {
  return useQuery({
    queryKey: ['tools'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/tools');
      if (error || !data) throw new Error('도구 목록을 읽지 못했다');
      return data;
    },
  });
}

/** 도구 한 글자 표시. 색이 둘 이상이면 그 순서의 그라데이션이다 */
export function ToolBadge({ tool, small }: { tool: Tool; small?: boolean }) {
  const colors = tool.colors.length ? tool.colors : ['#5b635b'];
  const background = colors.length === 1 ? colors[0] : `linear-gradient(135deg, ${colors.join(', ')})`;
  return (
    <span className={`tool ${small ? 'sm' : ''}`} style={{ background }} title={tool.title}>
      {tool.initial}
    </span>
  );
}

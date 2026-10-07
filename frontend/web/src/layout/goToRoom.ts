// 다른 곳(종·완료 알림)에서 방 하나로 데려간다. 지금 프로젝트 밖의 방이면 그 방의 프로젝트로 바꾼다
// 터미널 탭은 지금 프로젝트의 방만 보인다
import { useCurrentProject, useProjects, useSetCurrentProject } from '../project';
import { navigate } from '../router';
import { inProject, requestRoom, useRooms } from '../tabs/terminal/api';

export function useGoToRoom() {
  const rooms = useRooms();
  const current = useCurrentProject();
  const projects = useProjects();
  const setProject = useSetCurrentProject();
  return (id: string) => {
    const room = rooms.data?.find((r) => r.id === id);
    if (room && !inProject(room, current?.members ?? null)) {
      const owner = projects.data?.projects.find((p) => inProject(room, p.members));
      setProject.mutate(owner?.path ?? null);
    }
    requestRoom(id);
    navigate('terminal');
  };
}

// 아직 채우지 않은 탭의 빈 화면. 제목과 지금 프로젝트만 보여 준다
import { useCurrentProject } from '../project';
import { t, type StringKey } from '../strings';

export function Placeholder({ id, stage }: { id: string; stage: string }) {
  const project = useCurrentProject();
  return (
    <>
      <div className="phead">
        <h2>{t(`tab.${id}` as StringKey)}</h2>
      </div>
      <p className="faint">{project ? t('page.project', { name: project.name }) : t('page.allProjects')}</p>
      <div className="empty">{t('page.empty', { stage })}</div>
    </>
  );
}

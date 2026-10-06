// 왼쪽 메뉴. 탭 여섯 개와 뒤로·앞으로. 마우스 엄지 버튼은 브라우저가 알아서 뒤로·앞으로 보낸다
import { Icon } from '../icons/Icon';
import { navigate } from '../router';
import { t } from '../strings';
import { MENU_TABS } from '../tabs/registry';

export function Nav({ current }: { current: string }) {
  return (
    <nav className="nav" aria-label={t('app.name')}>
      <div className="nav-top">
        <button className="icon-btn" type="button" title={t('nav.back')} onClick={() => history.back()}>
          <Icon name="arrow-left" label={t('nav.back')} />
        </button>
        <button className="icon-btn" type="button" title={t('nav.forward')} onClick={() => history.forward()}>
          <Icon name="arrow-right" label={t('nav.forward')} />
        </button>
        <div className="logo">
          <i>D</i>
          <span>{t('app.name')}</span>
        </div>
      </div>
      <ul className="menu">
        {MENU_TABS.map((tab) => (
          <li key={tab.id}>
            <button
              type="button"
              aria-current={tab.id === current ? 'page' : undefined}
              title={tab.shortcut ? `${tab.title} (${tab.shortcut})` : tab.title}
              onClick={() => navigate(tab.id === 'dashboard' ? '' : tab.id)}
            >
              <Icon name={tab.icon} />
              <span className="lbl">{tab.title}</span>
            </button>
          </li>
        ))}
      </ul>
    </nav>
  );
}

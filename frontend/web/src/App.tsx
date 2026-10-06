// 앱 뼈대: 왼쪽 메뉴 + 위 줄 + 지금 탭. 단축키 Ctrl+1~6 은 탭, Ctrl+P 는 프로젝트 바꾸기
import { useEffect, useState } from 'react';
import { useServerEvents } from './api/events';
import { Nav } from './layout/Nav';
import { TopBar } from './layout/TopBar';
import { navigate, usePathId } from './router';
import { t } from './strings';
import { MENU_TABS, tabFor } from './tabs/registry';

export function App() {
  const tab = tabFor(usePathId());
  const connected = useServerEvents();
  const [pickerOpen, setPickerOpen] = useState(false);

  useEffect(() => {
    document.title = `${tab.title} · ${t('app.name')}`;
  }, [tab]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!event.ctrlKey || event.altKey || event.metaKey) return;
      const key = event.key.toLowerCase();
      if (key === 'p') {
        // 크롬에서는 인쇄 창을 막는다
        event.preventDefault();
        setPickerOpen(true);
        return;
      }
      const target = MENU_TABS.find((item) => item.shortcut === `Ctrl+${key}`);
      if (target) {
        event.preventDefault();
        navigate(target.id === 'dashboard' ? '' : target.id);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  const View = tab.View;
  return (
    <div className="app">
      <Nav current={tab.id} />
      <div className="main">
        <TopBar pickerOpen={pickerOpen} onPickerOpenChange={setPickerOpen} settingsOpen={tab.id === 'settings'} />
        <main className="page">
          <View />
        </main>
        {!connected && <div className="statusbar">{t('conn.lost')}</div>}
      </div>
    </div>
  );
}

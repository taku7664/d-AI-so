// 화면 시작점. 글꼴은 앱에 동봉한다 (docs/DECISIONS.md "아이콘·글꼴")
import '@fontsource/gothic-a1/400.css';
import '@fontsource/gothic-a1/500.css';
import '@fontsource/gothic-a1/600.css';
import '@fontsource/gothic-a1/700.css';
import '@fontsource/hahmlet/600.css';
import '@fontsource/hahmlet/700.css';
import '@fontsource/jetbrains-mono/400.css';
import '@fontsource/jetbrains-mono/500.css';
import './styles/tokens.css';
import './styles/app.css';
import './styles/parts.css';
import './styles/sessions.css';
import './styles/home.css';
import './styles/terminal.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';

// 서버 알림(/ws)으로 다시 받으므로 창에 돌아올 때마다 다시 받지 않는다
const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, staleTime: Infinity, retry: 1 } },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);

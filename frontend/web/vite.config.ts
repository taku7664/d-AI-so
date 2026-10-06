// 빌드한 화면은 Daiso.Host 가 내준다(DAISO_WEB_ROOT). 개발 서버로 띄우면 출처가 달라 보안 미들웨어가 막으므로 빌드해서 본다
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  build: { outDir: 'dist', emptyOutDir: true },
});

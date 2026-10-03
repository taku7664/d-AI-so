# 단계 계획

Electron 전환을 Stage 0~8로 나누고, 단계마다 할 일과 완료 기준을 정한다.
왜 이렇게 하는지는 [DECISIONS.md](DECISIONS.md), 일하는 방식과 진행 상황은 [README.md](README.md)에 있다.

## Stage 0 — 루트 솔루션 뼈대

- 루트에 `Daiso.sln`, `Directory.Build.props`, `global.json`(옛 값 그대로)을 만든다
- 새 솔루션이 `old/src`의 백엔드 프로젝트와 `old/tests`의 백엔드 테스트 세 벌을 참조한다. `Daiso.App`은 넣지 않는다
- `old/tools/run-app.ps1`로 옛 앱이 여전히 뜨는지 확인한다
- **완료 기준:** 루트에서 `dotnet build` · `dotnet test` 초록, `old/`에서도 초록

## Stage 1 — 서버 뼈대와 보안

- `src/Daiso.Server/` (ASP.NET Core minimal API). DI 구성과 UI 의존 없는 서비스를 옮긴다 ([MIGRATION_MAP.md](MIGRATION_MAP.md))
- [SECURITY.md](SECURITY.md)의 일곱 가지 전부, 그리고 서버를 띄우는 순서
- `GET /api/health`, OpenAPI 문서, 공용 WebSocket `/ws`
- `tests/Daiso.Server.Tests/`: 보안 규칙을 하나씩 깨 보는 테스트
- **완료 기준:** 보안 테스트 초록. 크롬에서 토큰 주소로 열면 health가 보이고, 토큰 없이 열면 거절된다

## Stage 2 — Electron 껍데기

- `desktop/` (Electron 메인 + preload, TypeScript). 루트 `package.json`의 workspace로 묶는다
- 서버를 자식 프로세스로 띄우고 주소를 읽어 창에 연다. 앱이 끝나면 서버도 끈다
- 두 번째 실행 막기, 트레이, 창 X → 트레이로 숨기기, 트레이 메뉴 "브라우저로 열기"
- 새 앱을 띄우는 `tools/run-app.ps1`을 루트에 새로 만든다 (옛 것은 `old/tools/`에 그대로)
- **완료 기준:** 빌드한 Electron 앱이 서버를 띄우고 빈 화면을 보여 준다. 앱을 끄면 서버 프로세스가 남지 않는다. `npm run lint` 초록

## Stage 3 — 화면 뼈대와 탭 계약

- `web/` (Vite + React + TS). 시안의 왼쪽 메뉴·색·글꼴을 CSS 변수로 옮긴다. 글꼴 파일은 동봉한다
- [TAB_PLUGINS.md](TAB_PLUGINS.md)의 탭 계약, 라우팅, `Ctrl+1~7` 단축키
- OpenAPI → TS 클라이언트 생성, `/ws` 알림 → 캐시 무효화(TanStack Query)
- 문구를 `web/src/strings/ko.json`으로 옮기고 키 검사 vitest를 만든다
- **완료 기준:** 일곱 탭이 빈 화면으로 뜨고 메뉴·단축키로 오간다. 크롬 탭과 Electron 창에서 똑같이 보인다

## Stage 4 — 사용량 탭

- 첫 수직 조각이다. 읽기만 하는 화면이라 서버·타입 생성·캐시·알림 흐름을 처음 끝까지 꿰기 좋다
- **완료 기준:** 옛 사용량 화면과 숫자가 같다 (같은 인덱스로 나란히 띄워 비교)
- **여기서 C# 서버를 계속 갈지 판단한다** ([DECISIONS.md](DECISIONS.md) "정한 것"의 버린 안)

## Stage 5 — 요약 · 세션 탭

- 세션: 검색(FTS5), 필터, 대화 보기, 이어서 열기, Markdown 내보내기, 휴지통으로 보내기, 실행 중 세션 지우기 차단
- **완료 기준:** 옛 세션 화면의 기능 목록을 하나씩 대조해 빠진 것이 없다

## Stage 6 — 터미널 탭

- 서버 `/ws/pty/{room}` ↔ xterm.js. 방 목록, 새 터미널 단계 카드, 설치 버튼, 외부 터미널로 열기
- Electron 창에서는 파일 끌어 놓기 → 경로 입력, 클립보드 이미지 붙여넣기
- **옛 터미널 쪽 fix 커밋(30개)을 훑어 같은 문제가 다시 나지 않는지 하나씩 확인한다**
- **완료 기준:** Claude·Codex 방을 열고, 앱을 트레이로 숨겼다 다시 열어도 출력이 이어진다

## Stage 7 — 내 규칙 · 내 프롬프트 · 설정

- 규칙·프롬프트는 **폴더가 원본**이다. 서버가 FileSystemWatcher로 감시하고 바뀌면 `/ws`로 알린다
- 폴더 위치를 설정에서 고를 수 있게 한다. 기본값은 옛 `AppPaths`의 `presets`·`prompts`다
- 시작 전에 폴더가 겹칠 때의 우선순위를 정한다 ([DECISIONS.md](DECISIONS.md) "미정")
- **완료 기준:** 탐색기나 AI가 폴더의 파일을 고치면 화면이 저절로 바뀐다

## Stage 8 — 배포 전환과 old 제거

- `old/docs/RELEASE.md`를 `docs/`로 옮기고 §2 표를 먼저 고친다. Windows App SDK·WebView2 줄을 빼고 Electron 줄을 넣는다
- 시작 전에 설치 프로그램 도구를 정한다 ([DECISIONS.md](DECISIONS.md) "미정")
- 백엔드 프로젝트와 테스트를 `old/src`·`old/tests`에서 루트 `src/`·`tests/`로 `git mv`한다
- 필요한 옛 문서를 `docs/`로 옮긴 뒤 `old/`를 지운다
- **완료 기준:** RELEASE §5 확인 목록을 깨끗한 Windows 10 Home에서 통과한다

## 위험과 되돌리기

| 위험 | 대응 |
|---|---|
| ViewModel 로직을 옮기다 동작이 어긋난다 | 옮기기 전에 그 로직을 Core/Infrastructure로 내리고 테스트부터 붙인다. 옛 앱과 새 앱을 같은 인덱스로 나란히 띄워 비교한다 |
| 보안 규칙이 빠진 엔드포인트가 생긴다 | 검사를 미들웨어 한 곳에 둔다. 보안 테스트가 모든 경로를 돈다 |
| 설치 파일이 커진다 (Chromium 동봉) | 받아들인다. 대신 WebView2 부트스트래퍼와 Windows App SDK가 빠진다 |
| 서버 프로세스가 고아로 남는다 | 서버가 부모 프로세스를 지켜보다 스스로 끝난다. Stage 2 완료 기준에 넣었다 |
| C# 서버에서 막힌다 | Stage 4에서 판단한다 |
| 새 서버를 위해 백엔드를 고치다 옛 앱이 깨진다 | 백엔드를 고친 커밋은 `old/` 솔루션 빌드·테스트도 돌린다 |
| 되돌리기 | Stage 7까지 `old/`가 그대로 있으므로 언제든 멈출 수 있다. Stage 8이 되돌리기 어려운 단계다 |

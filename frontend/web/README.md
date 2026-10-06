# web (화면)

Electron 창과 크롬 탭이 같이 보는 화면이다. Vite로 빌드하고, 빌드한 `dist/`를 `Daiso.Host`가 내준다.
배치·색·글꼴은 [docs/design/README.md](../../docs/design/README.md)의 D안을 따른다.

## 빌드하고 보기

```powershell
tools/run-app.ps1
```

저장소 루트에서 실행한다. Host·desktop·web을 빌드하고, `dist/` 경로를 `DAISO_WEB_ROOT`로 넘겨 띄운다.
`npm run dev`(Vite 개발 서버)는 쓰지 않는다. 출처가 Host와 달라져 보안 미들웨어가 요청을 막는다 ([docs/SECURITY.md](../../docs/SECURITY.md)).

| 명령 (`frontend/`에서) | 무엇 |
|---|---|
| `npm run build:web` | 타입 검사 뒤 `web/dist`로 빌드 |
| `npm test` | vitest. 지금은 문구 키 검사 |
| `npm run gen:api -w @daiso/web` | `src/api/openapi.json`에서 `src/api/schema.gen.ts`를 다시 만든다 |

## 서버 타입이 바뀌었을 때

1. `backend/`에서 `DAISO_UPDATE_OPENAPI=1`을 걸고 `dotnet test --filter OpenApiSnapshotTests`. `src/api/openapi.json`을 새로 쓴다
2. `frontend/`에서 `npm run gen:api -w @daiso/web`
3. 두 파일을 같이 커밋한다

서버를 바꾸고 1을 빼먹으면 백엔드의 `OpenApiSnapshotTests`가 빨개진다.

## 폴더

| 경로 | 무엇 |
|---|---|
| `src/main.tsx` | 시작점. 글꼴·CSS를 싣고 TanStack Query를 건다 |
| `src/App.tsx` | 왼쪽 메뉴 + 위 줄 + 지금 탭. 단축키 `Ctrl+1~6`, `Ctrl+P` |
| `src/router.ts` | 주소로 탭을 가른다. `/`는 요약, `/{탭 id}`는 그 탭 |
| `src/project.ts` | 지금 프로젝트(`/api/projects`). 값의 주인은 서버 설정이다 |
| `src/api/` | 생성한 API 타입과 클라이언트, `/ws` 알림 → 캐시 무효화 |
| `src/layout/` | 메뉴, 위 줄, 프로젝트 선택기, 빈 탭 화면 |
| `src/tabs/` | 탭마다 화면 모듈. 약속은 [src/tabs/README.md](src/tabs/README.md) |
| `src/strings/ko.json` | 화면 문구 전부. 없는 키는 타입 검사가, 안 쓰는 키는 `strings.test.ts`가 잡는다 |
| `src/styles/tokens.css` | 색·글꼴 변수. 시안의 `:root`와 같다. 테마는 시스템 · 밝게(`data-theme="light"`) · 어둡게(`data-theme="dark"`) |

## 캐시와 알림

- 쿼리 키 첫 칸은 서버 알림의 `tab` 이름과 같다(`['projects']`, `['sessions', ...]`). 알림 `{ "tab": "projects" }`가 오면 `['projects']`로 시작하는 쿼리를 다시 받는다
- 알림으로 다시 받으므로 창에 돌아올 때 다시 받지 않는다(`refetchOnWindowFocus: false`)
- `/ws`가 끊기면 0.5초부터 10초까지 늘려 가며 다시 잇고, 다시 붙으면 전부 다시 받는다. 끊긴 동안 아래에 한 줄 알린다
- 지금 프로젝트를 바꾸면 전부 다시 받는다. 탭마다 프로젝트에 따라 다른 것을 보여 주기 때문이다

## 패키지

`PROJECT_RULES.daiso`의 "꼭 필요한지 먼저 설명하고 추가한다"에 따라 적는다.

| 패키지 | 왜 |
|---|---|
| `react`, `react-dom` | 화면 스택 ([docs/DECISIONS.md](../../docs/DECISIONS.md)) |
| `@tanstack/react-query` | 서버 데이터 캐시와 알림 → 무효화 (docs/ROADMAP.md Stage 3) |
| `openapi-fetch` | 생성한 타입으로 요청하는 얇은 fetch 래퍼. 타입을 손으로 쓰지 않는다 |
| `bootstrap-icons` (MIT) | 아이콘. 스프라이트 SVG 하나를 쓴다 |
| `@fontsource/gothic-a1`, `hahmlet`, `jetbrains-mono` (OFL-1.1) | 글꼴 동봉. 앱이 인터넷으로 받지 않는다 |
| `vite`, `@vitejs/plugin-react` (개발) | 빌드 |
| `openapi-typescript` (개발) | OpenAPI → TS 타입 |
| `vitest` (개발) | 문구 키 검사 |

글꼴(OFL-1.1)과 아이콘(MIT)의 라이선스는 Stage 8에서 배포 문서에 같이 적는다.

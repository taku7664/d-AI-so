# 새 구조

프로세스가 어떻게 나뉘고, 코드가 저장소 어디에 놓이고, 상태를 누가 들고 있는지 정한다.
Core 모델과 파싱 규칙은 여전히 `old/docs/ARCHITECTURE.md`가 정본이다. 이 문서는 그 위의 App 계층만 다룬다.

## 프로세스 구성

```text
 ┌─ Electron 창 (기본) ─┐     ┌─ 크롬 탭 (AI 조작용) ─┐
 │  frontend/web 화면   │     │  같은 화면             │
 └─────────┬──────────┘     └──────────┬────────────┘
           └──── http://127.0.0.1:{port} ┘   HTTP = 요청 · WebSocket = 알림·터미널 출력
                        │
 ┌─ Daiso.Host (C#, ASP.NET Core Kestrel) ───────────────────┐
 │  /api/{탭 id}/...   탭마다 엔드포인트 묶음                   │
 │  /api/projects 등   탭에 속하지 않는 공용 경로                │
 │  Core · Providers · Infrastructure  (old/src 그대로)       │
 └───────────────────────────────────────────────────────────┘
```

| 프로세스 | 맡는 것 |
|---|---|
| Electron 메인 (`frontend/desktop/`) | 서버를 띄우고 끄기, 창, 트레이, 두 번째 실행 막기, 파일 끌어 놓기 경로, 알림, "브라우저로 열기" |
| 웹 화면 (`frontend/web/`) | 화면과 화면 상태만. 도메인 데이터는 서버에서 받아 캐시로만 든다 |
| `Daiso.Host` (`backend/src/Daiso.Host/`) | 도메인 데이터의 유일한 주인. 세션 인덱스, 설정, PTY, 규칙·프롬프트 파일, 플러그인 |

서버를 띄우는 순서와 보안은 [SECURITY.md](SECURITY.md)에 있다.

## 저장소 구조

```text
docs/                          여러 모듈에 걸친 문서
frontend/                      TypeScript 전부. npm 은 여기서만 돈다
  package.json                 npm workspaces 로 desktop·web 을 묶는다. 지금은 desktop 만
  eslint.config.mjs            desktop·web 공용 ESLint 설정
  .prettierrc.json             Prettier 설정 (.md 는 건드리지 않는다)
  desktop/                     Electron 메인 + preload (Stage 2)
  web/                         웹 화면 (Stage 3). 빌드한 dist/ 를 Host 가 내준다
    src/tabs/{id}/             탭마다 화면 모듈
    src/api/openapi.json       서버 OpenAPI 스냅숏. schema.gen.ts 를 여기서 만든다
backend/                       C# 전부. dotnet 은 여기서만 돈다
  Daiso.sln                    새 솔루션. 백엔드 라이브러리는 Stage 8 전까지 old/src 를 참조한다 (Stage 0)
  src/Daiso.Host/              C# 프로세스: 보안 미들웨어, /ws, DI (Stage 1)
  src/Daiso.StatusLine/        Claude 상태줄 명령. 구독 한도를 파일로 남긴다 (Stage 4)
    Tabs/{Id}/                 탭마다 엔드포인트 묶음
    Shared/                    탭에 속하지 않는 공용 경로 (/api/projects)
  tests/                       새 테스트
tools/                         빌드·실행 스크립트 (Stage 2에서 새 run-app.ps1). 언어에 묶이지 않는 PowerShell
.editorconfig                  들여쓰기·줄 끝. old/ 에도 걸리므로 C# 분석기 규칙은 넣지 않는다
PROJECT_RULES.daiso            코딩 규칙 정본 (C#·TS 공통)
old/                           2026-10-03 까지의 WinUI 앱 전부
```

## 상태의 주인

| 상태 | 주인 | 예 |
|---|---|---|
| 도메인 데이터 | 서버 | 세션 목록, 인덱스, 설정, 인증 상태, PTY 출력, 플러그인 목록 |
| 화면 상태 | 웹 | 입력 중인 글자, 선택한 줄, 펼친 단계, 스크롤 위치 |
| 지금 프로젝트 | 웹이 들고, 마지막 값은 서버 설정에 남긴다 | 다시 켜면 마지막에 고른 프로젝트로 연다 |
| 세션·방 이름 | 서버 (자료 폴더 `session-names.json`) | 도구가 쓰는 세션 파일과 인덱스 DB는 고치지 않는다 |
| 구독 한도 | **도구가 남긴 파일.** 서버는 읽기만 한다 | Codex 세션 기록의 `rate_limits`, DAIso 상태줄 스크립트가 남긴 Claude `rate_limits` |
| 규칙·프롬프트 원본 | **파일 시스템** | 서버는 폴더를 감시해 다시 읽기만 한다. DB에 사본을 두지 않는다 |

## 화면과 서버가 주고받는 법

1. 웹 → 서버는 **요청**이다 (`GET /api/sessions/list`, `POST /api/rules/save`)
2. 서버 → 웹은 **알림**이다. 공용 WebSocket `/ws`로 "무엇이 바뀌었다"만 보낸다
3. 웹은 알림을 받으면 그 데이터를 **무조건 다시 받는다.** 요청과 알림의 순서 꼬임은 이걸로 막는다
4. 터미널 출력처럼 계속 흐르는 것은 따로 연다 (`/api/terminal/rooms/{id}/pty` WebSocket. 탭 경로 안에 두어 탭 약속을 지킨다)
5. 요청·응답 타입은 서버의 OpenAPI 문서에서 TS로 생성한다. 손으로 적지 않는다

## 탭에 속하지 않는 공용 경로

위 줄(프로젝트 선택기·종·계정)은 어느 탭에 있든 보인다. 그래서 그 데이터는 탭 경로가 아니라 공용 경로에 둔다.

| 경로 | 무엇 | 쓰는 곳 |
|---|---|---|
| `/api/projects` | 아는 프로젝트 목록 (옛 `KnownProjects`), 프로젝트마다 마지막 작업 시각 | 프로젝트 선택기, "모든 프로젝트" 요약 |
| `/api/accounts` | 도구마다 로그인 상태(토큰 값 없음), 다시 로그인(새 터미널 창), 저장한 계정 저장·바꾸기·지우기 | 위 줄 계정 단추, 요약의 손볼 것 |
| `/api/limits` | 도구마다 구독 한도와 기준 시각. `PUT /claude/statusline`으로 Claude 상태줄 등록을 켜고 끈다 | 사용량, 계정 단추 |
| `/api/tools` | 도구 목록(플러그인 포함), 표시 순서·글자·색 | 도구 칩, 도구 표시 |
| `/api/index` | 인덱스 갱신 상태. `POST /refresh`·`/rebuild`로 시작한다. 서버가 뜰 때 한 번 갱신한다 | 아래 줄, "다시 읽기" |

- 터미널 방 상태(종)는 터미널 탭 경로(`/api/terminal/...`)를 그대로 쓴다. 공용 경로를 따로 두지 않는다
- 공용 경로도 보안 미들웨어 한 곳을 지난다. 알림은 `{ "tab": "projects", "kind": "changed" }`처럼 경로 이름을 `tab` 자리에 넣어 보낸다
- 공용 경로는 여러 탭이 같이 쓰는 데이터만 둔다. 한 탭만 쓰면 그 탭 경로에 둔다

## Electron 창과 크롬 탭이 다른 것

| 기능 | Electron 창 | 크롬 탭 |
|---|---|---|
| 탐색기에서 파일 끌어 놓기 → 전체 경로 | `webUtils.getPathForFile`로 경로를 받는다 | 브라우저가 경로를 주지 않는다. **끌어 놓기를 끈다** |
| 폴더 "찾아보기" | `dialog.showOpenDialog` | 서버가 폴더 목록을 주는 자체 선택기 (Electron도 이걸 기본으로 써도 된다) |
| 트레이 상주, 창 X로 숨기기 | Electron `Tray` | 해당 없음 (탭을 닫아도 서버는 Electron이 붙들고 있다) |
| 완료 알림 | Electron `Notification` | 웹 알림 (권한을 물음) |
| 클립보드 이미지 붙여넣기 | 웹 클립보드 API → 서버가 파일로 저장 | 같음 |
| 외부 터미널 창으로 열기 | 서버가 띄운다 | 같음 |

Electron 전용 기능은 전부 `window.daisoDesktop` 하나로 들어온다. preload가 `contextBridge`로 연다. 화면은 이 객체가 있는지만 보고 기능을 켜고 끈다.

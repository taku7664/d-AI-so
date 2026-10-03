# Electron 프런트엔드 전환 계획

> 2026-10-03. WinUI 3 화면(`Daiso.App`)을 걷어 내고 **Electron + 웹 화면**으로 바꾼다. C# 백엔드(Core · Providers · Infrastructure)는 그대로 쓴다.
> 이 문서는 무엇을 어디로 옮기고, 어떤 순서로 하며, 무엇을 처음부터 못 박아야 하는지를 정한다.
> 화면 시안: https://claude.ai/artifact/DDRz5Lv416wn35na37WCPS (요약 · 터미널 · 세션 세 장)

---

## 0. 시작하는 사람에게

### 먼저 읽을 것

1. `CLAUDE.md` — 빌드·커밋 규칙
2. `docs/ARCHITECTURE.md` §1 — 계층 구조. **이 계획은 App 계층만 갈아 끼운다.** Core 순수성 규칙은 그대로다
3. `docs/RELEASE.md` — 배포 원칙. Stage 8에서 §2 표를 고친다
4. 이 문서 §3(정한 것) · §4(보안) · §8(단계)

### 일하는 방식

- **Stage 순서대로 한다.** 앞 단계의 완료 기준을 채우기 전에 다음을 시작하지 않는다
- **단계마다 커밋하고 `git push origin main`**
- **WinUI 앱은 Stage 8 전까지 지우지 않는다.** 새 앱이 기능을 다 따라잡을 때까지 지금 앱이 배포 가능한 상태로 남아 있어야 한다
- **§3의 결정은 다시 논의하지 않는다.** 바꾸려면 §3에 이유를 적고 바꾼다

### 지금 어디까지 됐나

**아직 시작 전** (2026-10-03).

---

## 1. 왜 바꾸나

| 이유 | 내용 |
|---|---|
| 화면 품질 | WinUI 레이아웃이 마음에 들지 않는다. HTML/CSS는 AI가 다루기 쉽고 디자인을 빨리 바꿀 수 있다 |
| AI가 화면을 직접 조작 | 같은 화면을 크롬 탭에서 열 수 있으면 AI가 브라우저 도구로 눌러 보고 확인할 수 있다 |
| 생태계 | 컴포넌트·차트·터미널(xterm.js) 같은 화면 라이브러리가 JS 쪽에 훨씬 많다 |
| 성능 요구 | 높지 않다. Chromium 무게를 감수할 만하다 |

**버그가 난 자리 (2026-10-03, `git log`로 셈).** 커밋 284개 중 `fix`가 98개다. fix 커밋이 건드린 파일은 `Daiso.App` 271개, Infrastructure 22개, Core 20개, Providers 6개 합계 34개다.
버그 대부분이 WinUI 화면과 창 관리에서 났다. 그래서 백엔드는 C#으로 남기고 화면만 바꾼다.

---

## 2. 목표 구조

```text
 ┌─ Electron 창 (기본) ─┐     ┌─ 크롬 탭 (AI 조작용) ─┐
 │  web/ 화면          │     │  같은 web/ 화면        │
 └─────────┬──────────┘     └──────────┬────────────┘
           └──── http://127.0.0.1:{port} ┘   HTTP = 요청 · WebSocket = 알림·터미널 출력
                        │
 ┌─ Daiso.Server (C#, ASP.NET Core Kestrel) ─────────────────┐
 │  /api/{탭 id}/...   탭마다 엔드포인트 묶음                   │
 │  Core · Providers · Infrastructure  (지금 코드 그대로)      │
 └───────────────────────────────────────────────────────────┘
```

### 프로세스마다 하는 일

| 프로세스 | 맡는 것 |
|---|---|
| Electron 메인 (`desktop/`) | 서버를 띄우고 끄기, 창, 트레이, 두 번째 실행 막기, 파일 끌어 놓기 경로, 알림, "브라우저로 열기" |
| 웹 화면 (`web/`) | 화면과 화면 상태만. 도메인 데이터는 서버에서 받아 캐시로만 든다 |
| `Daiso.Server` (`src/Daiso.Server/`) | 도메인 데이터의 유일한 주인. 세션 인덱스, 설정, PTY, 규칙·프롬프트 파일, 플러그인 |

### 상태의 주인은 한 곳

| 상태 | 주인 | 예 |
|---|---|---|
| 도메인 데이터 | 서버 | 세션 목록, 인덱스, 설정, 인증 상태, PTY 출력, 플러그인 목록 |
| 화면 상태 | 웹 | 입력 중인 글자, 선택한 줄, 펼친 단계, 스크롤 위치 |
| 규칙·프롬프트 원본 | **파일 시스템** | 서버는 폴더를 감시해 다시 읽기만 한다. DB에 사본을 두지 않는다 |

- 웹 → 서버는 **요청**이다 (`GET /api/sessions/list`, `POST /api/rules/save`)
- 서버 → 웹은 **알림**이다. "무엇이 바뀌었다"만 보내고, 웹은 그 데이터를 다시 받아 온다
- 알림을 받으면 무조건 다시 받는다. 요청과 알림의 순서 꼬임은 이걸로 막는다

---

## 3. 정한 것

| 결정 | 이유 | 버린 안 |
|---|---|---|
| **Electron이 기본 앱** | 사람은 앱으로 쓴다. 크롬 탭은 AI에게 시킬 때만 쓴다 | 브라우저 전용 · WebView2 셸 |
| **백엔드는 C# 그대로** | §1의 버그 통계. 다 고쳐 둔 파서·인덱스·테스트를 버리지 않는다 | 백엔드까지 TypeScript로 다시 쓰기 (C#에서 막히면 그때 다시 본다) |
| **화면 ↔ 서버는 HTTP + WebSocket** | 크롬 탭에서도 똑같이 돌아야 한다. Electron IPC를 쓰면 크롬 탭에서 안 된다 | Electron IPC · stdio 통신 |
| **Electron 전용 기능은 좁은 다리 하나로** | `window.daisoDesktop`(preload)이 있을 때만 켠다. 없으면(크롬) 기능을 줄여 보여 준다 | 화면 곳곳에서 Electron API 직접 호출 |
| **WinUI와 병행하다 한 번에 넘어간다** | 한 창에 XAML과 웹 화면을 섞을 수 없다. 새 앱이 다 따라잡으면 Stage 8에서 바꾼다 | 화면을 하나씩 옮기며 섞어 쓰기 |
| 화면 스택: **Vite + React + TypeScript** | 자료와 예제가 가장 많고 AI가 가장 잘 다룬다 | 미정이던 것을 이 문서에서 정한다. 바꾸려면 Stage 3 전에 |
| 서버 ↔ 웹 타입: **OpenAPI에서 TS 타입 생성** | C# record와 TS 타입이 어긋나면 런타임에야 터진다. 손으로 두 벌 적지 않는다 | 손으로 맞추기 |

---

## 4. 서버 보안 규칙 — Stage 1에 전부 넣는다

이 서버는 프로세스를 띄우고(PTY) 파일을 휴지통으로 보낸다. 막지 않으면 **크롬에서 연 아무 웹사이트**가 `127.0.0.1`로 요청을 보내 이 기능을 부를 수 있다(CSRF, DNS rebinding). 나중에 붙이면 빠지는 엔드포인트가 생기므로 첫 단계에 다 넣는다.

1. **`127.0.0.1`에만 바인딩한다.** `0.0.0.0`, `localhost`(IPv6 해석 차이) 금지. 포트는 0으로 받아 OS가 고르게 한다
2. **실행할 때마다 토큰을 새로 만든다.** Electron 메인이 32바이트 난수를 만들어 환경 변수로 서버에 넘긴다
3. **첫 접속만 `/?token=...`으로 받는다.** 서버가 `HttpOnly; SameSite=Strict` 쿠키로 바꾸고 주소에서 토큰을 지워 다시 보낸다. 이후 HTTP·WebSocket은 쿠키로 확인한다
4. **`Host` 헤더가 `127.0.0.1:{port}`가 아니면 거절한다** (DNS rebinding 차단)
5. **상태를 바꾸는 요청과 WebSocket은 `Origin`도 확인한다.** 자기 주소가 아니면 거절한다
6. **CORS를 열지 않는다.** `Access-Control-Allow-Origin`을 보내지 않는다
7. 서버 주소와 토큰은 `%LOCALAPPDATA%\DAIso\server.json`에 쓴다. 같은 사용자의 AI가 크롬 탭을 열 때 이 파일을 읽는다. 서버가 꺼지면 지운다

완료 기준에 **이 일곱 가지를 하나씩 깨 보는 테스트**가 들어간다 (Stage 1).

---

## 5. Electron 창과 크롬 탭이 다른 것

| 기능 | Electron 창 | 크롬 탭 |
|---|---|---|
| 탐색기에서 파일 끌어 놓기 → 전체 경로 | `webUtils.getPathForFile`로 경로를 받는다 | 브라우저가 경로를 주지 않는다. **끌어 놓기를 끈다** |
| 폴더 "찾아보기" | `dialog.showOpenDialog` | 서버가 폴더 목록을 주는 자체 선택기 (Electron도 이걸 기본으로 써도 된다) |
| 트레이 상주, 창 X로 숨기기 | Electron `Tray` | 해당 없음 (탭을 닫아도 서버는 Electron이 붙들고 있다) |
| 완료 알림 | Electron `Notification` | 웹 알림 (권한을 물음) |
| 클립보드 이미지 붙여넣기 | 웹 클립보드 API → 서버가 파일로 저장 | 같음 |
| 외부 터미널 창으로 열기 | 서버가 띄운다 | 같음 |

Electron 전용 기능은 전부 `window.daisoDesktop`(preload에서 `contextBridge`로 연다) 하나로 들어온다. 화면은 이 객체가 있는지만 보고 기능을 켜고 끈다.

---

## 6. 탭 플러그인 계약

VS Code처럼 **탭 하나 = 화면 모듈 하나 + 서버 엔드포인트 묶음 하나**로 만든다. 기본 탭 일곱 개(요약 · 터미널 · 세션 · 내 규칙 · 내 프롬프트 · 사용량 · 설정)부터 이 계약대로 만든다.

### 화면 쪽 (`web/src/tabs/{id}/index.ts`)

```ts
export const tab: TabModule = {
  id: 'sessions',          // 소문자. 서버 경로 /api/sessions/... 와 같다
  title: '세션',
  icon: 'list',
  order: 30,
  shortcut: 'Ctrl+3',
  View: SessionsView,      // React 컴포넌트
};
```

### 서버 쪽 (`src/Daiso.Server/Tabs/{Id}/`)

```csharp
public interface ITabEndpoints
{
    string Id { get; }                                  // 화면 쪽 id 와 같다
    void Map(RouteGroupBuilder group);                  // /api/{Id} 아래에 엔드포인트를 단다
}
```

알림은 공용 WebSocket 하나(`/ws`)로 보낸다. 메시지는 `{ "tab": "sessions", "kind": "changed" }` 꼴이다.

### 범위

- **기본 탭이 실제로 쓰는 만큼만 계약에 넣는다.** 쓰이지 않는 확장 지점을 미리 만들지 않는다
- 바깥 플러그인이 탭을 더하는 길은 **이 계획에 넣지 않는다** (§10). 계약이 기본 탭 일곱 개로 굳은 뒤에 따로 계획한다
- 지금 있는 도구 플러그인(`Providers.Manifest`, 어댑터 프로세스)은 그대로 서버가 읽는다. 탭 플러그인과 다른 것이다

---

## 7. 지금 App 코드를 어디로 옮기나

`Daiso.App` 안에 화면이 아닌 로직이 꽤 있다. 줄 수와 UI 의존(`Microsoft.UI` · `DispatcherQueue` · 문구 리소스 참조 수)은 2026-10-03에 센 값이다.

### 서버로 옮기는 것

| 지금 | 줄 수 | 갈 곳 |
|---|---|---|
| `App.xaml.cs` 의 DI 구성 (99~186줄) | — | `Daiso.Server/Program.cs`. ViewModel 등록은 빼고 서비스만 |
| `Services/IndexService.cs` | 98 | 서버 (UI 의존 3곳을 걷어 낸다) |
| `Services/SettingsStore.cs` · `AppSettings.cs` | 125 · 78 | 서버. UI 의존 없음 |
| `Services/KnownProjects.cs` | 69 | 서버. UI 의존 없음 |
| `Services/ToolRegistry.cs` · `ToolPluginCatalog.cs` | 31 · 95 | 서버. UI 의존 없음 |
| `Services/CrashReporter.cs` | 172 | 서버 몫과 Electron 몫으로 나눈다 |
| `Terminal/TerminalHost.cs` 의 PTY 연결 부분 | 535 중 일부 | 서버 `/ws/pty/{room}`. WebView2 부분은 버린다 |
| `ViewModels/RoomManager.cs` | 170 | 서버. 방 목록은 도메인 상태다 |
| 각 ViewModel 안의 도메인 로직 | — | 서버 엔드포인트. **아래 표의 큰 ViewModel 셋이 이 작업의 대부분이다** |

### 다시 쓰는 것 (ViewModel → 서버 엔드포인트 + React 화면)

| ViewModel | 줄 수 | UI 의존 |
|---|---|---|
| `SessionsViewModel` | 1070 | 26 |
| `TerminalViewModel` | 988 | 11 |
| `RuleMakerViewModel` | 842 | 15 |
| `DashboardViewModel` | 680 | 17 |
| `PromptsViewModel` · `UsageViewModel` · `SettingsViewModel` | 325 · 326 · 320 | 16 · 4 · 9 |

ViewModel을 옮길 때마다 줄 하나하나를 **도메인 로직(서버로) / 화면 상태(웹으로) / 버림(WinUI 전용)** 셋 중 하나로 가른다. 도메인 로직은 가능하면 Core나 Infrastructure로 내려서 테스트를 붙인다.

### Electron으로 옮기는 것

| 지금 | 줄 수 |
|---|---|
| `Services/TrayIcon.cs` | 392 |
| `Services/FileDropTarget.cs` | 211 |
| `Services/DoneNotifier.cs` 의 알림 표시 부분 | 160 중 일부 |
| `App.xaml.cs` 의 두 번째 실행 막기 | — |

### 버리는 것

XAML 전부, `Controls/`, `Ui/`, `DialogHost`, `Navigator`, `NavigationHistory`, `ToolLook`의 브러시 부분.
`Daiso.Core.Tests/Architecture/` 의 XAML 검사 테스트(`GridPlacementTests`, `ItemTemplateTests`, `PageSkeletonTests`, `ThemeBrushTests`, `ToolNameInXamlTests`, `UiTokenTests` 등)도 Stage 8에서 같이 지운다. 같은 목적의 검사가 웹 쪽에 필요하면 그때 vitest로 만든다.

---

## 8. 단계

### Stage 0 — App에서 화면 아닌 서비스를 떼어 낸다

- §7 "서버로 옮기는 것" 중 서비스(IndexService, SettingsStore, KnownProjects, ToolRegistry, ToolPluginCatalog, RoomManager)를 새 라이브러리 `Daiso.Application`(net8.0-windows)으로 옮긴다
- WinUI 앱은 이 라이브러리를 참조해 **지금과 똑같이** 돈다
- **완료 기준:** 테스트 세 벌 초록, `tools/run-app.ps1`로 띄운 앱이 이전과 같다

### Stage 1 — 서버 뼈대와 보안

- `src/Daiso.Server/` (ASP.NET Core minimal API). `Daiso.Application`의 DI 구성을 그대로 쓴다
- §4 일곱 가지 전부. `GET /api/health`, OpenAPI 문서, 공용 WebSocket `/ws`
- 시작하면 표준 출력에 `DAISO_LISTENING http://127.0.0.1:{port}` 한 줄을 쓴다. 부모 프로세스가 죽으면 스스로 끝난다
- `tests/Daiso.Server.Tests/` — §4를 하나씩 깨 보는 테스트 (토큰 없음, 틀린 Host, 다른 Origin, 쿠키 없는 WebSocket 등)
- **완료 기준:** 보안 테스트 초록. 크롬에서 토큰 주소로 열면 health가 보이고, 토큰 없이 열면 거절된다

### Stage 2 — Electron 껍데기

- `desktop/` (Electron 메인 + preload, TypeScript)
- 서버를 자식 프로세스로 띄우고 주소를 읽어 창에 연다. 앱이 끝나면 서버도 끈다
- 두 번째 실행 막기, 트레이, 창 X → 트레이로 숨기기, 트레이 메뉴 "브라우저로 열기"
- 개발 중에는 Vite 개발 서버를 연다. `tools/run-app.ps1`이 새 앱도 띄울 수 있게 고친다
- **완료 기준:** 빌드한 Electron 앱이 서버를 띄우고 빈 화면을 보여 준다. 앱을 끄면 서버 프로세스가 남지 않는다

### Stage 3 — 화면 뼈대와 탭 계약

- `web/` (Vite + React + TS). 시안의 왼쪽 메뉴·색·글꼴을 CSS 변수로 옮긴다. 글꼴 파일은 동봉한다
- §6 탭 계약, 라우팅, `Ctrl+1~7` 단축키, OpenAPI → TS 클라이언트 생성, `/ws` 알림 → 캐시 무효화(TanStack Query)
- 문구: `Strings/ko-KR/Resources.resw` → `web/src/strings/ko.json`. `StringResourceKeysTests`와 같은 일을 하는 vitest를 만든다
- **완료 기준:** 일곱 탭이 빈 화면으로 뜨고 메뉴·단축키로 오간다. 크롬 탭과 Electron 창에서 똑같이 보인다

### Stage 4 — 사용량 탭 (첫 수직 조각)

- 읽기만 하는 화면이라 서버·타입 생성·캐시·알림 흐름을 처음 끝까지 꿰기 좋다
- **완료 기준:** WinUI 사용량 화면과 숫자가 같다 (같은 인덱스로 나란히 띄워 비교)

### Stage 5 — 요약 · 세션 탭

- 세션: 검색(FTS5), 필터, 대화 보기, 이어서 열기, Markdown 내보내기, 휴지통으로 보내기, 실행 중 세션 지우기 차단
- **완료 기준:** WinUI 세션 화면의 기능 목록을 하나씩 대조해 빠진 것이 없다

### Stage 6 — 터미널 탭

- 서버 `/ws/pty/{room}` ↔ xterm.js. 방 목록, 새 터미널 단계 카드, 설치 버튼, 외부 터미널로 열기
- Electron 창에서는 파일 끌어 놓기 → 경로 입력, 클립보드 이미지 붙여넣기
- **지금 터미널 쪽에 쌓인 fix 커밋(30개)을 훑어 같은 문제가 다시 나지 않는지 하나씩 확인한다**
- **완료 기준:** Claude·Codex 방을 열고, 앱을 트레이로 숨겼다 다시 열어도 출력이 이어진다

### Stage 7 — 내 규칙 · 내 프롬프트 · 설정

- 규칙·프롬프트는 **폴더가 원본**이다. 서버가 FileSystemWatcher로 감시하고 바뀌면 `/ws`로 알린다
- 폴더 위치를 설정에서 고를 수 있게 한다 (기본값은 지금 `AppPaths`의 `presets`·`prompts`). 프로젝트 폴더 안의 `PROJECT_RULES.daiso`·`docs/prompts`와 겹칠 때 어느 쪽이 이기는지는 **미정**. 이 단계 시작 전에 정한다
- **완료 기준:** 탐색기나 AI가 폴더의 파일을 고치면 화면이 저절로 바뀐다

### Stage 8 — 배포 전환과 WinUI 제거

- `docs/RELEASE.md` §2 표를 먼저 고친다: Windows App SDK·WebView2 줄을 빼고 Electron 줄을 넣는다. 설치 프로그램 도구(Inno 유지 / electron-builder NSIS)는 **미정**. 둘 다 관리자 권한 없이 사용자 폴더에 깔 수 있어야 한다
- `Daiso.App` 프로젝트와 XAML 검사 테스트를 지운다. `ARCHITECTURE.md` §1·§5를 새 구조로 다시 쓴다
- **완료 기준:** RELEASE §5 확인 목록을 깨끗한 Windows 10 Home에서 통과한다

---

## 9. 위험과 되돌리기

| 위험 | 대응 |
|---|---|
| ViewModel 로직을 옮기다 동작이 어긋난다 | 옮기기 전에 그 로직을 Core/Infrastructure로 내리고 테스트부터 붙인다. WinUI와 새 앱을 같은 인덱스로 나란히 띄워 비교한다 |
| 보안 규칙이 빠진 엔드포인트가 생긴다 | 검사를 엔드포인트마다가 아니라 미들웨어 한 곳에 둔다. 보안 테스트가 모든 경로를 돈다 |
| 설치 파일이 커진다 (Chromium 동봉) | 받아들인다 (§1 성능 요구 낮음). 대신 WebView2 부트스트래퍼와 Windows App SDK가 빠진다 |
| 서버 프로세스가 고아로 남는다 | 서버가 부모 프로세스를 지켜보다 스스로 끝난다. Stage 2 완료 기준에 넣었다 |
| C# 서버에서 막힌다 | Stage 4까지 해 보고 판단한다. 백엔드를 TS로 옮기는 안은 §3에 버린 안으로 남아 있다 |
| 되돌리기 | Stage 7까지 WinUI 앱이 그대로 남아 있으므로 언제든 멈출 수 있다. Stage 8이 되돌리기 어려운 단계다 |

---

## 10. 안 하기로 한 것 · 미정

### 안 하기로 한 것

- **바깥 플러그인이 탭을 더하는 길** — 계약이 기본 탭으로 굳은 뒤에 따로 계획한다
- **화면 ↔ 서버에 Electron IPC 쓰기** — 크롬 탭에서 안 된다 (§3)
- **XAML과 웹 화면 섞어 쓰기** — 한 창에서 섞을 수 없다 (§3)
- **원격 접속** — 서버는 `127.0.0.1`에서만 받는다. 다른 PC에서 여는 기능은 만들지 않는다

### 미정

- 규칙·프롬프트 폴더가 겹칠 때 우선순위 — Stage 7 시작 전
- 설치 프로그램 도구 (Inno 유지 / electron-builder) — Stage 8 시작 전
- 라이트 테마 — 시안은 어두운 테마만 있다. Stage 3에서 CSS 변수로 자리만 만든다

---

## 이 문서를 덮고 할 일

Stage 0부터 시작한다. 첫 커밋은 `Daiso.Application` 라이브러리를 만들고 서비스를 옮기는 것이며, WinUI 앱 동작은 바뀌지 않아야 한다.

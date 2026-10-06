# 문서 안내

WinUI 3 앱을 **Electron + 웹 화면 + C# 서버**로 옮기는 작업의 문서다. 무엇을 어디서 찾는지, 어떻게 일하는지, 지금 어디까지 왔는지를 적는다.
2026-10-03까지의 앱과 문서는 전부 `old/`에 있다.

## 지금 어디까지 됐나

| 날짜 | 한 일 |
|---|---|
| 2026-10-03 | 작업물을 `old/`로 옮김. 문서를 주제별로 나눔. 최상위를 `frontend/`(TS)·`backend/`(C#)로 나누고 ESLint·Prettier는 `frontend/`에 둠 |
| 2026-10-06 | **Stage 4 완료.** 진짜 Claude 설정으로 켜고 끄기(끄면 바이트 그대로 돌아오게 고침), 옛 앱과 사용량 화면을 나란히 비교(같음), 두 앱 동시 인덱스 갱신 이상 없음. Claude 한도 보기는 주인 PC에 켜 둠. 하다가 켜기 확인 단계가 끈 뒤에도 남던 화면 버그를 고침 |
| 2026-10-06 | **Stage 4 구독 한도 코드 끝.** Codex는 세션 기록에서, Claude는 새 상태줄 명령(`backend/src/Daiso.StatusLine`, 63~70ms)이 남긴 파일에서 읽음. 켜기 전에 동의를 묻고, 끄면 원래 설정으로 돌림. 하다가 부모가 이미 없을 때 Host가 뜨는 도중에 꺼져 시작이 취소 예외로 끝나던 경쟁을 고침. 전체 시험 중 Providers 시험 하나가 한 번 실패했는데 다시 돌려 보니 재현되지 않아 어느 것인지 못 찾음 |
| 2026-10-06 | **Stage 4 토큰 부분 끝, 구독 한도 남음.** 사용량 화면(오늘·7일·30일, 일·주·월 그래프, 도구 칩, 이 프로젝트/모든 프로젝트, 프로젝트별·모델별). Host에 `/api/usage`·`/api/tools`·`/api/index`(뜰 때 한 번 갱신)와 아래 줄 인덱스 상태. 숫자를 인덱스에 SQL로 직접 센 값과 맞춤 |
| 2026-10-06 | **Stage 3 완료 (크롬 탭은 주인이 틈틈이 확인).** Electron 창을 CDP로 눌러 메뉴·단축키·톱니·프로젝트 바꾸기·다시 켜도 유지를 확인함. 하다가 `Ctrl+P` 방향키 초점 문제와, 인덱스가 깨졌을 때 프로젝트 목록이 500을 내던 것을 고침. **사용자 `index.db-wal`이 깨져 있는 것을 발견**(원인 모름). 주인 허락으로 깨진 파일을 `index-broken-20261006\`에 옮겨 두고 인덱스를 다시 만듦(세션 570) |
| 2026-10-06 | **Stage 3 코드 끝.** `frontend/web`(React 19·Vite 8·TanStack Query): 위 줄(프로젝트 선택기, 종·계정 자리, 톱니), 탭 여섯 개와 설정 빈 화면, `Ctrl+1~6`·`Ctrl+P`, D안 색·글꼴·아이콘. Host가 화면 빌드를 내주고 `/api/projects`를 엶. OpenAPI 스냅숏 시험으로 서버와 TS 타입을 맞춤. 하다가 시험 Host가 진짜 사용자 인덱스를 열 수 있던 것을 임시 폴더로 막음 |
| 2026-10-06 | **Stage 1·2 완료.** `frontend/desktop`(Electron 44.5.1): Host를 띄워 창에 열기, 두 번째 실행 막기, 트레이, X로 숨기기, 브라우저로 열기. 루트 `tools/run-app.ps1`. 하다가 Electron이 끝날 때 Host가 잡 오브젝트에 묶여 강제로 죽는 바람에 `server.json`이 남는 것을 찾아, Host를 잡 밖에서 띄우게 함 |
| 2026-10-06 | **화면 시안을 D안으로 정함.** "지금 프로젝트"가 앱 전체의 기준, 탭 여섯 개(설정은 톱니), 요약은 프로젝트 첫 화면, 단가표·$ 추정을 없애고 구독 한도를 파일로 읽음. 기본 탭 id 확정. 시안 HTML을 `docs/design/`에 넣고 ROADMAP Stage 3~7에 반영. Stage 1 크롬 확인 중 "토큰 없이 열면 거절"은 확인함 |
| 2026-10-05 | **Stage 1 코드·테스트 끝, 크롬 확인 남음.** `backend/src/Daiso.Host`(net10.0-windows): 보안 미들웨어, `server.json`, 부모 감시, `/api/health`·OpenAPI·`/ws`, 옛 앱의 서비스 등록. 보안 테스트 48개. 하다가 SQLite 취약점(CVE-2025-6965) 때문에 `Infrastructure`의 SQLitePCLRaw를 2.1.13으로 올림 |
| 2026-10-05 | **Stage 0 완료.** `backend/`에 `Daiso.sln`·`Directory.Build.props`·`global.json`. 솔루션은 `old/src` 백엔드 7개와 `old/tests` 세 벌, 테스트가 참조하는 `old/tools/adapters/Daiso.Adapter.Claude`를 담는다. 하다가 빨갛던 옛 테스트 하나(어댑터가 일찍 끝나면 파이프 쓰기에서 던짐)을 고침 |

다음은 [ROADMAP.md](ROADMAP.md) Stage 5(요약·세션)다. 새 앱은 루트에서 `tools/run-app.ps1`로 띄운다 ([frontend/web/README.md](../frontend/web/README.md)).

### 이어받는 사람이 먼저 볼 것

- Stage 1 전에 정할 것은 다 정했다: Host TFM은 `net10.0-windows`, 자료 폴더는 옛 앱과 같이 쓴다 ([DECISIONS.md](DECISIONS.md) "정한 것")
- `old/` 안 문서에 적힌 경로(`src/...`, `docs/...`, `tools/...`)는 **`old/` 기준**이다. 옮기면서 고치지 않았다
- 화면 시안은 `docs/design/daiso-d.html`이다. 같은 내용의 아티팩트 링크는 저장소 주인 계정의 비공개 링크라 열리지 않을 수 있다
- 확인 명령: 옛 앱은 `old/`에서 `dotnet build Daiso.sln`·`dotnet test Daiso.sln`, 새 백엔드는 `backend/`에서 `dotnet build`·`dotnet test`, 프런트 설정은 `frontend/`에서 `npm ci` 뒤 `npm run lint`·`npm run format:check`

## 문서 목록

| 문서 | 무엇을 정하나 | 언제 읽나 |
|---|---|---|
| [ROADMAP.md](ROADMAP.md) | Stage 0~8, 단계마다 완료 기준, 위험과 되돌리기 | 일을 시작할 때 |
| [DECISIONS.md](DECISIONS.md) | 왜 바꾸나, 정한 것과 버린 안, 안 하기로 한 것, 미정 | 방향을 바꾸고 싶을 때 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 프로세스 구성, 저장소 구조, 상태의 주인, 창과 크롬 탭의 차이 | 코드를 어디에 둘지 정할 때 |
| [SECURITY.md](SECURITY.md) | localhost 서버 보안 규칙 일곱 가지 | 서버 엔드포인트를 만들 때 |
| [MIGRATION_MAP.md](MIGRATION_MAP.md) | 옛 `Daiso.App` 코드가 서버·Electron·웹 중 어디로 가는지 | 옛 화면을 옮길 때 |
| [design/README.md](design/README.md) | 화면 시안(D안)과 시안에서 정한 화면 규칙 | 화면을 만들 때 |

한 모듈에만 해당하는 약속은 그 모듈 폴더의 `README.md`에 있다.

| 문서 | 무엇 |
|---|---|
| [frontend/desktop/README.md](../frontend/desktop/README.md) | Electron 메인: 띄우기, Host와 주고받는 것, 끌 때 Host가 남지 않는 이유 |
| [frontend/web/README.md](../frontend/web/README.md) | 화면: 빌드하고 보기, 서버 타입이 바뀌었을 때, 캐시와 알림, 패키지 |
| [frontend/web/src/tabs/README.md](../frontend/web/src/tabs/README.md) | 탭의 화면 쪽 약속, 기본 탭 id 목록 |
| [backend/src/Daiso.Host/Tabs/README.md](../backend/src/Daiso.Host/Tabs/README.md) | 탭의 서버 쪽 약속, 알림 보내는 법 |

옛 문서 중 아직 정본인 것:

| 문서 | 무엇 |
|---|---|
| `old/docs/ARCHITECTURE.md` | Core 모델, 파싱 규칙, Core 순수성 규칙. 백엔드는 여전히 이걸 따른다 |
| `old/docs/RELEASE.md` | 배포 원칙. Stage 8에서 `docs/`로 옮겨 고친다 |
| `old/docs/PLUGIN_PLAN.md` | 도구 플러그인(매니페스트·어댑터). 탭 플러그인과는 다른 것이다 |

화면 시안: [design/README.md](design/README.md) (D안. 저장소 안에 HTML이 있다)

## 일하는 방식

- **Stage 순서대로 한다.** 앞 단계의 완료 기준을 채우기 전에 다음을 시작하지 않는다
- **단계마다 커밋하고 `git push origin main`.** 이 표의 "지금 어디까지 됐나"도 같이 고친다
- **`old/`는 Stage 8 전까지 지우지 않는다.** 새 앱이 다 따라잡을 때까지 옛 앱이 빌드·배포 가능해야 한다
- **`old/src/Daiso.App`은 얼린다.** 급한 배포가 아니면 고치지 않는다. 필요한 로직은 읽고 새 쪽에 다시 쓴다
- **백엔드(`old/src/Daiso.Core` · `Providers.*` · `Infrastructure`)는 옛 앱과 새 서버가 같이 쓴다.** 고치면 `old/` 솔루션의 빌드·테스트도 초록이어야 한다
- **[DECISIONS.md](DECISIONS.md)에 적힌 결정은 다시 논의하지 않는다.** 바꾸려면 그 문서에 이유를 적고 바꾼다
- **`docs/`에는 여러 모듈에 걸친 약속만 둔다.** 한 모듈에만 해당하는 약속은 그 모듈 폴더의 `README.md`에 쓴다
- 코딩 규칙은 루트 `PROJECT_RULES.daiso`가 정본이다. ESLint(`frontend/eslint.config.mjs`)는 그중 기계가 잡을 수 있는 것만 건다

## 이 문서를 덮고 할 일

[ROADMAP.md](ROADMAP.md)를 열고 Stage 1의 남은 확인부터 한다.

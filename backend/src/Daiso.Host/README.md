# Daiso.Host

화면(Electron 창·크롬 탭)이 붙는 C# 서버다. `127.0.0.1`에서만 받고, 도메인 데이터의 유일한 주인이다.
이 문서는 Host를 띄우는 법과 띄울 때 주고받는 약속을 적는다. 보안 규칙 자체는 [docs/SECURITY.md](../../../docs/SECURITY.md)가 정본이다.

## 혼자 띄우기 (Electron 없이)

`backend/`에서 실행한다. dotnet이 `backend/global.json`을 찾아야 한다.

```bash
dotnet run --project src/Daiso.Host
```

표준 출력에 두 줄이 나온다. `DAISO_OPEN` 뒤의 주소를 크롬으로 열면 토큰이 쿠키로 바뀌고 주소에서 사라진다.

```text
DAISO_LISTENING http://127.0.0.1:51234
DAISO_OPEN http://127.0.0.1:51234/?token=...
```

같은 내용이 `%LOCALAPPDATA%\DAIso\server.json`에도 남는다. Host가 꺼지면 지운다. 다른 Host가 그 뒤에 덮어썼으면 지우지 않는다.

## Electron이 띄울 때 넘기는 것

| 환경 변수 | 무엇 | 없으면 |
|---|---|---|
| `DAISO_TOKEN` | 이번 실행의 토큰. 32자 이상 | Host가 직접 만들고 `DAISO_OPEN` 줄도 쓴다 |
| `DAISO_PARENT_PID` | Electron 메인 PID. 이 프로세스가 끝나면 Host도 끝난다 | 부모를 지켜보지 않는다 |
| `DAISO_WEB_ROOT` | 웹 화면 빌드 폴더(`frontend/web/dist`). `tools/run-app.ps1`이 넘긴다 | 화면 없이 안내 페이지만 낸다 |

- Host는 `DAISO_TOKEN`을 읽자마자 자기 환경에서 지운다. Host가 띄우는 터미널(PTY)이 토큰을 물려받지 않게 하려는 것이다
- Electron은 `DAISO_LISTENING ` 으로 시작하는 줄을 기다렸다가 그 주소에 `/?token=...`을 붙여 창에 연다
- 토큰이 32자보다 짧거나 PID가 숫자가 아니면 Host는 뜨지 않는다
- 부모가 Node(Electron)라면 Host를 `detached`로 띄워야 한다. 아니면 부모가 끝날 때 Windows 잡 오브젝트가 Host를 먼저 죽여서 정상 종료(`server.json` 지우기)를 못 한다 ([frontend/desktop/README.md](../../../frontend/desktop/README.md))

## 경로

| 경로 | 무엇 |
|---|---|
| `/` | 첫 화면. `?token=`은 여기서만 받는다. 웹 화면 폴더가 있으면 `index.html`, 없으면 링크 몇 개뿐인 안내 페이지 |
| `/{탭 id}` 등 점 없는 주소 | 웹 화면 폴더가 있으면 `index.html`. 화면이 주소로 탭을 가르기 때문이다. `/api/` 아래 없는 경로는 404 |
| `/assets/...` | 웹 화면 빌드 파일 |
| `/api/dashboard` | 요약 탭. `project`로 좁힌다. 오늘 세션 수, 최근 7일 토큰, 최근 세션 5개(마지막으로 친 질문 포함). `/api/dashboard/worktrees`는 프로젝트 저장소의 워크트리(git 을 그대로 부른다, 20초 캐시) |
| `/api/bell` | 위 줄 종 팝업. 손볼 것(로그인·정리·인덱스·플러그인), 마지막으로 하던 것, 다른 프로젝트에서 하던 것. 모든 프로젝트 기준 |
| `/api/projects`, `/api/tools`, `/api/index`, `/api/limits`, `/api/accounts` | 공용 경로 ([docs/ARCHITECTURE.md](../../../docs/ARCHITECTURE.md) "탭에 속하지 않는 공용 경로") |
| `/api/terminal` | 터미널 탭. 방 목록·열기·닫기·이름·본 표시, 모델 목록, 새 창으로 열기, 폴더 열기. 방 화면은 WebSocket `/api/terminal/rooms/{id}/pty`(서버→화면 바이너리, 화면→서버 `{"t":"in"}`·`{"t":"resize"}`) |
| `/api/sessions` | 세션 탭. 목록·검색·대화·내보내기·이름·지우기·이어서 열기·정리 기준. 경로로 받는 세션은 인덱스에 있는 것만 다룬다 |
| `/api/usage` | 사용량 탭. `grain`(day·week·month), `tool`, `project`로 좁힌다. 날짜는 UTC 기준(세션 기록이 UTC로 적힌다) |
| `/api/health` | `{"status":"ok"}` |
| `/openapi/v1.json` | OpenAPI 문서. 웹이 여기서 TS 타입을 만든다 (Stage 3) |
| `/ws` | 공용 알림 WebSocket. 서버 → 화면 한 방향 |
| `/api/{탭 id}/...` | 탭마다 엔드포인트. [Tabs/README.md](Tabs/README.md) |

모든 경로가 [Security/LocalOnlyMiddleware.cs](Security/LocalOnlyMiddleware.cs) 한 곳을 지난다. 새 경로를 달면 `backend/tests/Daiso.Host.Tests`의 보안 테스트가 그 경로도 저절로 돈다.

## 파일

| 파일 | 무엇 |
|---|---|
| `Program.cs` | 환경 변수를 읽어 띄운다. 그것뿐이다 |
| `DaisoHost.cs` | 조립. `Program`과 테스트가 같은 조립을 쓴다 |
| `DomainServices.cs` | 옛 앱 `App.xaml.cs`의 서비스 등록을 옮긴 것. ViewModel은 뺐다 |
| `Security/` | 토큰, 보안 미들웨어, `server.json`, 주소 알리기 |
| `Notifications/` | `/ws` 알림 허브 |
| `Services/` | 옛 `Daiso.App/Services`에서 옮긴 것: `SettingsStore`·`AppSettings`, `ToolRegistry`, `ToolPluginCatalog` |
| `Shared/` | 탭에 속하지 않는 공용 경로. `ProjectCatalog`(세션 인덱스 + 최근 폴더) |
| `Shared/Limits.cs` | 구독 한도: Claude 상태줄 켜고 끄기(`ClaudeStatusLine`), 도구마다 한도 모으기, 한도 파일 지켜보기. 상태줄 명령은 [../Daiso.StatusLine/README.md](../Daiso.StatusLine/README.md) |
| `Tabs/Terminal/` | 방(`Room`), 여러 화면에 흘리는 출력(`RoomOutput`), 방 띄우기·상태 훅·상태 파일 지켜보기(`RoomService`). ConPTY 는 옛 `Infrastructure/Pty` 를 그대로 쓴다 |
| `Services/IndexService.cs` | 인덱스 갱신을 한 번에 하나만 돌리고 진행·끝을 `/ws`로 알린다. 서버가 뜰 때 한 번 갱신한다 |
| `Services/IndexWatcher.cs` | 도구들의 세션 폴더를 지켜보다 바뀐 파일만 인덱스에 다시 읽힌다(1.5초 조용하면, 길어도 6초 안에). 다른 터미널에서 친 채팅도 요약·세션에 따라온다 |
| `Tabs/` | 탭 엔드포인트 |

## 옛 앱과 같이 쓰는 것

자료 폴더 `%LOCALAPPDATA%\DAIso`를 옛 앱과 같이 쓴다 ([docs/DECISIONS.md](../../../docs/DECISIONS.md) "자료 폴더").

- `settings.json`: 두 앱 모두 시작할 때 한 번 읽고 저장할 때 통째로 쓴다. 동시에 띄워 둔 채 양쪽에서 설정을 바꾸면 나중에 저장한 쪽만 남는다. `AppSettings`의 칸을 바꿀 때는 옛 앱 쪽 칸도 같이 본다. 이쪽이 모르는 칸은 `AppSettings.Unknown`이 들고 있다가 다시 쓴다
- `index.db`: 두 인스턴스가 동시에 다시 만들어도 깨지지 않는 것을 테스트로 확인했다 (`old/tests/Daiso.Infrastructure.Tests`의 `Two_indexes_on_one_file_can_write_at_the_same_time`). 테스트 자료가 작아 잠금이 짧은 경우만 봤다. 한쪽이 쓰기 잠금을 오래 잡을 때 다른 쪽 쓰기가 어떻게 되는지는 아직 모른다. 인덱스를 쓰는 탭(Stage 4)에서 실제 크기 인덱스로 본다

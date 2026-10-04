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

- Host는 `DAISO_TOKEN`을 읽자마자 자기 환경에서 지운다. Host가 띄우는 터미널(PTY)이 토큰을 물려받지 않게 하려는 것이다
- Electron은 `DAISO_LISTENING ` 으로 시작하는 줄을 기다렸다가 그 주소에 `/?token=...`을 붙여 창에 연다
- 토큰이 32자보다 짧거나 PID가 숫자가 아니면 Host는 뜨지 않는다

## 경로

| 경로 | 무엇 |
|---|---|
| `/` | 첫 화면. `?token=`은 여기서만 받는다. Stage 3 전까지는 링크 몇 개뿐이다 |
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
| `Tabs/` | 탭 엔드포인트 |

## 옛 앱과 같이 쓰는 것

자료 폴더 `%LOCALAPPDATA%\DAIso`를 옛 앱과 같이 쓴다 ([docs/DECISIONS.md](../../../docs/DECISIONS.md) "자료 폴더").

- `settings.json`: 두 앱 모두 시작할 때 한 번 읽고 저장할 때 통째로 쓴다. 동시에 띄워 둔 채 양쪽에서 설정을 바꾸면 나중에 저장한 쪽만 남는다. `AppSettings`의 칸을 바꿀 때는 옛 앱 쪽 칸도 같이 본다. 이쪽이 모르는 칸은 `AppSettings.Unknown`이 들고 있다가 다시 쓴다
- `index.db`: 두 인스턴스가 동시에 다시 만들어도 깨지지 않는 것을 테스트로 확인했다 (`old/tests/Daiso.Infrastructure.Tests`의 `Two_indexes_on_one_file_can_write_at_the_same_time`). 테스트 자료가 작아 잠금이 짧은 경우만 봤다. 한쪽이 쓰기 잠금을 오래 잡을 때 다른 쪽 쓰기가 어떻게 되는지는 아직 모른다. 인덱스를 쓰는 탭(Stage 4)에서 실제 크기 인덱스로 본다

# 옛 코드 이전 지도

옛 `old/src/Daiso.App`의 코드가 서버·Electron·웹 중 어디로 가는지 정한다. 아래 경로는 모두 그 폴더 기준이다.
줄 수와 UI 의존(`Microsoft.UI` · `DispatcherQueue` · 문구 리소스 참조 수)은 2026-10-03에 센 값이다.

## 서버로 옮기는 것

| 지금 | 줄 수 | 갈 곳 |
|---|---|---|
| `App.xaml.cs`의 DI 구성 (99~186줄) | — | `backend/src/Daiso.Host/Program.cs`. ViewModel 등록은 빼고 서비스만 |
| `Services/IndexService.cs` | 98 | 서버 (UI 의존 3곳을 걷어 낸다) |
| `Services/SettingsStore.cs` · `AppSettings.cs` | 125 · 78 | 서버. UI 의존 없음 |
| `Services/KnownProjects.cs` | 69 | 서버. UI 의존 없음 |
| `Services/ToolRegistry.cs` · `ToolPluginCatalog.cs` | 31 · 95 | 서버. UI 의존 없음 |
| `Services/CrashReporter.cs` | 172 | 서버 몫과 Electron 몫으로 나눈다 |
| `Terminal/TerminalHost.cs`의 PTY 연결 부분 | 535 중 일부 | 서버 `/ws/pty/{room}`. WebView2 부분은 버린다 |
| `ViewModels/RoomManager.cs` | 170 | 서버. 방 목록은 도메인 상태다 |
| 각 ViewModel 안의 도메인 로직 | — | 서버 엔드포인트. 아래 "다시 쓰는 것" 참고 |

UI 의존이 없는 서비스(SettingsStore, KnownProjects, ToolRegistry, ToolPluginCatalog)는 Stage 1에 가져온다. 나머지는 그 서비스를 쓰는 탭을 만들 때 가져온다.

## 다시 쓰는 것

ViewModel은 서버 엔드포인트와 React 화면으로 나눠 다시 쓴다. **이 작업의 대부분이 여기다.**

| ViewModel | 줄 수 | UI 의존 | 탭 (Stage) |
|---|---|---|---|
| `SessionsViewModel` | 1070 | 26 | 세션 (5) |
| `TerminalViewModel` | 988 | 11 | 터미널 (6) |
| `RuleMakerViewModel` | 842 | 15 | 내 규칙 (7) |
| `DashboardViewModel` | 680 | 17 | 요약 (5) |
| `UsageViewModel` | 326 | 4 | 사용량 (4) |
| `PromptsViewModel` | 325 | 16 | 내 프롬프트 (7) |
| `SettingsViewModel` | 320 | 9 | 설정 (7) |

옮기는 절차:

1. ViewModel의 줄을 **도메인 로직(서버로) / 화면 상태(웹으로) / 버림(WinUI 전용)** 셋 중 하나로 가른다
2. 도메인 로직은 가능하면 Core나 Infrastructure로 내리고 테스트부터 붙인다
3. 서버 엔드포인트를 만들고, 웹 화면은 생성된 타입으로 그 엔드포인트만 부른다
4. 옛 앱과 새 앱을 같은 인덱스로 나란히 띄워 결과를 비교한다

## Electron으로 옮기는 것

| 지금 | 줄 수 |
|---|---|
| `Services/TrayIcon.cs` | 392 |
| `Services/FileDropTarget.cs` | 211 |
| `Services/DoneNotifier.cs`의 알림 표시 부분 | 160 중 일부 |
| `App.xaml.cs`의 두 번째 실행 막기 | — |

## 웹으로 옮기는 것

| 지금 | 갈 곳 |
|---|---|
| `Strings/ko-KR/Resources.resw` | `frontend/web/src/strings/ko.json`. `StringResourceKeysTests`와 같은 일을 하는 vitest를 만든다 |
| `Assets/xterm/` | `web` 의존성 `@xterm/xterm`으로 바꾼다 |

## 버리는 것

- XAML 전부, `Controls/`, `Ui/`, `DialogHost`, `Navigator`, `NavigationHistory`, `ToolLook`의 브러시 부분
- `old/tests/Daiso.Core.Tests/Architecture/`의 XAML 검사 테스트(`GridPlacementTests`, `ItemTemplateTests`, `PageSkeletonTests`, `ThemeBrushTests`, `ToolNameInXamlTests`, `UiTokenTests` 등). Stage 8에서 `old/`와 같이 사라진다. 같은 목적의 검사가 웹 쪽에 필요하면 그때 vitest로 만든다

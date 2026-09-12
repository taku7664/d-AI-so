# 배포 규칙

설치 프로그램이 **무엇을 담고 무엇을 담지 않는지**를 정한다. 의존성을 더하거나 빼려면 이 문서를 먼저 고치고 코드가 따른다.
만드는 방법은 `tools/make-installer.ps1` 한 줄이고, 설치 프로그램 정의는 `tools/installer/daiso.iss` 다.

## 1. 원칙

1. **받는 사람은 설치 프로그램 하나만 받는다.** "먼저 X 를 깔고 오세요"를 앱 실행에 대해서는 말하지 않는다.
2. **앱이 뜨는 데 필요한 것은 담는다. 앱이 다루는 도구를 깔기 위한 것은 담지 않는다.** 앱은 AI CLI 를 다루는 도구이고, 그 CLI 와 CLI 의 런타임을 대신 깔아 주는 것은 앱의 일이 아니다. 대신 없으면 **어디서 받는지 열어 준다.**
3. **사용자 PC 의 시스템 설정을 바꾸지 않는다.** PowerShell 실행 정책, PATH 레지스트리, 백신 예외 같은 것은 앱도 설치 프로그램도 건드리지 않는다. 그 설정이 걸리면 우리 쪽에서 피해 간다 (예: `.ps1` 대신 `.cmd` 를 부른다, ARCHITECTURE §5.3).
4. **관리자 권한 없이 깔린다.** 사용자 폴더에 깔리고 UAC 창이 뜨지 않는다. 동봉한 설치기가 스스로 권한을 올리는 것은 그 설치기의 일이다.
5. **남의 스크립트를 대신 돌리지 않는다.** `irm … | iex` 꼴 설치는 안내 페이지를 열고 사람이 한다 (`IProvider.InstallUri`).

## 2. 의존성마다 어떻게 하는가

| 의존성 | 왜 필요한가 | 처리 | 어디에 |
|---|---|---|---|
| .NET 8 런타임 | 앱 자체 | **앱에 담는다.** `SelfContained=true`. 별도 설치기도 확인 코드도 없다 | `Daiso.App.csproj` |
| Windows App SDK 1.8 | WinUI 3 | **앱에 담는다.** `WindowsAppSDKSelfContained=true` | `Daiso.App.csproj` |
| ConPTY (OpenConsole) | 내장 터미널 | **앱에 담는다.** NuGet 패키지가 `x64\OpenConsole.exe`·`conpty.dll` 을 복사한다 | `Daiso.App.csproj` |
| WebView2 런타임 | 내장 터미널(xterm.js) | **Evergreen 부트스트래퍼(약 2MB)를 동봉하고 없는 PC 에서만 설치 중에 돌린다.** 실패해도 앱 설치는 계속한다 — 앱은 WebView2 가 없으면 내장 터미널 대신 새 창으로 CLI 를 띄운다 | `daiso.iss` `[Files]`·`[Run]`, `WebView2Found` |
| Node.js (npm) | Claude Code·Codex 를 npm 으로 깔 때 | **담지 않는다.** 설치 버튼을 눌렀는데 npm 이 없으면 Node.js 내려받기 페이지를 열고, Node 를 깐 뒤 앱을 다시 켜라고 말한다 (이미 뜬 앱의 PATH 사본에는 새 npm 이 없다) | `TerminalViewModel.InstallAsync`, `ToolLaunchViewModel.PrerequisitePageFor` |
| Claude Code · Codex · Antigravity | 앱이 다루는 도구 | **담지 않는다.** 앱 안의 설치 버튼이 npm 명령을 새 터미널에서 돌리거나(Claude·Codex) 공식 안내 페이지를 연다(Antigravity) | `IProvider.InstallCommand`·`InstallUri` |

### 담지 않기로 한 것과 이유

- **Node.js MSI(약 30MB).** 넣으면 UAC 창이 하나 늘고, 이 앱이 Node 를 관리하는 앱이 된다. Node 는 CLI 의 런타임이지 앱의 런타임이 아니다. Claude Code 는 Node 없는 네이티브 설치기도 제공하므로 앞으로는 더 덜 필요해진다.
- **.NET 런타임 설치기 동봉.** 자체 포함이 더 단순하다. 설치기 실행도 UAC 도 런타임 감지 코드도 없어진다. 대신 산출물이 약 70MB 커진다. 받아들인다.
- **WebView2 Standalone(약 180MB).** 부트스트래퍼가 인터넷에서 받는 편이 훨씬 작다. 인터넷 없는 PC 는 이 앱의 대상이 아니다 — AI CLI 자체가 인터넷을 쓴다.

## 3. 만드는 절차

```powershell
.\tools\make-installer.ps1
```

스크립트가 하는 일, 순서대로:

1. `Directory.Build.props` 의 `Version` 을 읽는다. 판 번호의 정본은 여기 하나다.
2. 떠 있는 앱을 끄고 `dotnet build -c Release` 를 돈다. **`dotnet publish` 는 쓰지 않는다** — unpackaged WinUI 는 publish 에서 컴파일된 XAML(`App.xbf`)과 `DAIso.pri` 가 빠져 시작하자마자 죽는다 (2026-09-11 확인).
3. 산출물에 `App.xbf`(XAML 이 있다)와 `hostfxr.dll`(자체 포함이다)이 있는지 본다. 하나라도 없으면 멈춘다.
4. `artifacts\redist\MicrosoftEdgeWebview2Setup.exe` 가 없으면 Microsoft 고정 주소(`https://go.microsoft.com/fwlink/p/?LinkId=2124703`)에서 받고, **Authenticode 서명이 Microsoft 인지 확인한다.** 아니면 지우고 멈춘다. 저장소에는 넣지 않는다(`artifacts/` 는 gitignore).
5. Inno Setup 6 으로 `artifacts\installer\DAIso-{판}-setup.exe` 를 만들고 SHA256 을 찍는다.

> **이름이 바뀐 판(0.1.2~)**: 실행 파일이 `d-AI-so.exe` 에서 `DAIso.exe` 로, 설치 폴더가 `DAIso` 로 바뀌었다 (2026-09-12).
> 묶는 열쇠(`AppId`)는 그대로라 판올림은 같은 앱으로 이어지지만, **옛 폴더와 바로 가기가 남을 수 있다** — 올린 뒤 한 번 확인한다.
> **자료 폴더도 `%LOCALAPPDATA%\DAIso` 로 옮겨 간다.** 앱이 처음 뜰 때 옛 폴더를 통째로 옮긴다(`AppPaths`) — 인덱스·설정·계정 보관함이 그대로 따라온다.
> 같은 드라이브 안에서 이름만 바뀌므로 크기와 상관없이 한순간이고, 옮기지 못하면(파일이 잠겨 있으면) 옛 폴더를 계속 쓴다.

## 4. 올리는 곳

- 소스는 이 private 저장소에만 둔다.
- 공개 배포는 별도 public 저장소의 Release 에 **설치 프로그램 exe 와 SHA256** 만 올린다. 빌드 산출물 zip 은 올리지 않는다 — 설치 프로그램이 정본이다.
- 서명은 하지 않는다(인증서 없음). 받는 사람은 SmartScreen 경고를 한 번 본다. README 에 SHA256 을 적어 둔다.

## 5. 배포 전 확인

- [ ] `dotnet test` 가 세 프로젝트 모두 초록인가
- [ ] `CHANGELOG.md` 에 이 판의 항목이 있는가
- [ ] 설치 프로그램을 **깨끗한 Windows 10 Home** 에서 한 번 돌려 봤는가 — .NET 없음, WebView2 없음, Node 없음, 실행 정책 Restricted 인 상태가 기준이다
  - 앱이 뜬다
  - 터미널 화면에서 Claude 설치 버튼이 Node.js 내려받기 페이지를 연다
  - Node 를 깐 뒤 앱을 다시 켜면 **설치 버튼이 새 터미널에서 npm 을 실제로 돌린다** (`npm.ps1` 정책 오류가 없다 — 띄우기만 고치고 이것을 놓친 적이 있다)
  - Claude 를 깐 뒤 터미널 방이 열린다 (`claude.ps1` 정책 오류가 없다)
- [ ] 제거 뒤 `%LOCALAPPDATA%\DAIso` 를 지울지 **묻는지** 확인했는가. 조용한 제거에서는 묻지도 지우지도 않아야 한다

## 6. 바꿀 때

의존성을 더하거나 빼면 §2 표를 먼저 고친다. 그다음 `daiso.iss`·`make-installer.ps1`·코드를 표에 맞춘다. §5 확인 목록에 새 의존성의 "없는 PC" 항목을 넣는다.

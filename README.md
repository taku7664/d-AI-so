# d-AI-so

AI에 필요한게 다이소.

Claude Code, Codex CLI, Antigravity CLI를 Windows에서 한 창에 모아 쓴다.
로그인이 살아 있는지 보고, 지난 대화를 찾아 이어서 열고, 토큰을 얼마나 썼는지 확인한다.
CLI는 앱 안에서 바로 띄운다.

인터넷으로 아무것도 보내지 않는다. 도구들이 이 PC에 이미 만들어 둔 파일만 읽는다.

![요약](docs/screenshots/dashboard.png)

## 기능

| 화면 | 하는 일 |
|---|---|
| 요약 | 도구별 계정, 요금제, 만료일, 최근 세션. 토큰 값은 어디에도 표시하지 않는다 |
| 터미널 | 도구와 폴더를 고르면 앱 안에서 CLI가 뜬다. 새 창으로 빼도 된다 |
| 세션 | 지난 대화를 프로젝트별로 모아 본문까지 검색한다. 이어서 열기, markdown 내보내기 |
| 사용량 | 세션 기록으로 계산한 토큰 추이. 단가를 넣으면 비용도 추정한다 |
| 내 규칙 | 조건과 행동을 짜서 `PROJECT_RULES.daiso`를 만들고 `CLAUDE.md`, `AGENTS.md`에 넣는다 |
| 내 프롬프트 | 기획 인터뷰, 버그 재현, 회고 같은 프롬프트를 골라 프로젝트에 넣고 첫 메시지로 보낸다 |
| 설정 | 경로, 단가표, 테마, 그리고 [직접 추가한 도구](#도구-추가) 목록 |

| 터미널 | 세션 |
|---|---|
| ![터미널](docs/screenshots/terminal.png) | ![세션](docs/screenshots/sessions.png) |

| 사용량 | 내 규칙 |
|---|---|
| ![사용량](docs/screenshots/usage.png) | ![내 규칙](docs/screenshots/rules.png) |

## 빌드와 실행

.NET SDK 8 이상이 필요하다. 이 저장소는 SDK 10에서 `net8.0`을 타겟한다(`global.json`).

```bash
dotnet build Daiso.sln
```

경고 0, 오류 0이 정상이다(`TreatWarningsAsErrors=true`).
WinUI 앱은 x64로만 빌드되지만 솔루션이 `Any CPU`를 `x64`로 매핑해 두었으니 위 한 줄로 끝난다.

실행은 이 스크립트로 한다. 방금 빌드한 파일을 그대로 띄운다.

```bash
pwsh tools/run-app.ps1
```

산출물은 `src/Daiso.App/bin/Debug/net8.0-windows10.0.19041.0/win-x64/d-AI-so.exe`다.
설치가 필요 없고, Windows App SDK를 실행 파일에 담아서(`WindowsAppSDKSelfContained`)
PC에 깔린 런타임 버전과 무관하게 돈다. 창은 1024×700까지만 작아진다.

## 도구 추가

앱이 아는 도구는 세 개로 고정이 아니다.
`%USERPROFILE%\.daiso\tools\`에 YAML 파일을 넣으면 그 도구가 탭과 카드에 함께 올라온다.
다시 빌드할 필요 없고, 앱만 다시 켜면 된다.

![도구 플러그인](docs/screenshots/plugins.png)

### YAML 한 장으로 되는 것

이름과 색이 붙고, 터미널에서 그 도구를 띄울 수 있다.

```yaml
# %USERPROFILE%\.daiso\tools\mycli.yaml
schema: 1
id: mycli                                  # 소문자, 숫자, - 로 2~32자. 글자로 시작한다
name: My CLI
short: My                                  # 탭에 쓰는 짧은 이름
vendor: 우리팀
initial: M                                 # 로고 자리에 넣을 한 글자
color: "#7A5AF8"                           # 두 개 이상 적으면 그라데이션이 된다
executable: mycli.cmd                      # PATH 에서 찾는다
install:
  uri: https://example.com/mycli           # 안 깔려 있으면 이 주소를 안내한다
  # command: npm install -g mycli          # 설치 명령을 알려 줄 수도 있다
sessionsRoot: "{USERPROFILE}/.mycli/sessions"
rules:
  fileName: MYCLI.md                       # 이 도구가 읽는 프로젝트 지시문 파일
context:
  - "{PROJECT}/MYCLI.md"                   # 컨텍스트 점검이 찾아볼 자리
resume: "--resume {id}"                    # 이어서 열 때 붙일 인자
```

`id`는 계정 보관함의 폴더 이름이 된다. 한번 정하면 바꾸지 않는 게 좋다.
경로에는 `{USERPROFILE}`, `{PROJECT}`, `{HERE}`(이 YAML이 있는 폴더)를 쓸 수 있다.

더 적을 수 있는 항목은 `order`(표시 순서), `logoPath`(SVG 경로 데이터), `appendOnly`,
`imagePasteKeys`, `auth.files`(로그인 파일 목록), `models.list`(모델 선택 목록)다.

YAML이 틀렸을 때 도구가 조용히 사라지지는 않는다.
모르는 키, 빈 `id`, 내장 도구와 겹치는 `id`, 두 파일이 같은 `id`를 쓴 경우를
설정 화면이 이유와 함께 보여 준다. 한 파일이 깨져도 나머지는 그대로 실린다.

### 대화 기록까지 읽으려면

세션 목록, 검색, 사용량까지 되게 하려면 기록을 읽어 줄 프로그램을 하나 붙인다.

```yaml
adapter:
  command: "node {HERE}/mycli-adapter.js"
```

stdin으로 요청 한 줄을 받아서 stdout으로 답을 여러 줄 쓰면 된다.
한 줄이 JSON 하나이고 마지막 줄에 `Done`을 넣어 끝을 알린다. 언어는 상관없다.

| 받는 요청 | 보낼 답 |
|---|---|
| `{"V":1,"Op":"hello"}` | `{"V":1,"Ok":true,"Name":"...","Done":true}` |
| `{"V":1,"Op":"sessions","Root":"..."}` | `{"Session":{...}}` 여러 줄, 그리고 `{"Done":true}` |
| `{"V":1,"Op":"session","FilePath":"..."}` | `{"Session":{...},"Done":true}` |
| `{"V":1,"Op":"messages","FilePath":"...","FromByteOffset":0}` | `{"Message":{...}}` 여러 줄, 그리고 `{"ReadTo":1234,"Done":true}` |

`Session`에 넣을 값은 `Id`, `FilePath`, `ProjectPath`, `StartedAt`, `ModifiedAt`, `SizeBytes`,
`UserCount`, `AssistantCount`, `FirstPrompt`, `Usage`다.
`Message`는 `At`, `Role`, `Text`이고 `Role`은 `user`, `assistant`, `tool`, `system` 중 하나다.
모르는 값은 `system`으로 취급한다.

앱이 내보내는 키는 위처럼 대문자로 시작한다. 받을 때는 대소문자를 가리지 않으니
답은 `{"session": ...}`처럼 소문자로 써도 된다. 한글은 그대로 나가서 눈으로 읽힌다.

프로세스는 한 번 띄워 두고 요청마다 한 줄씩 주고받는다.
어댑터가 죽거나 30초 동안 한 줄도 안 보내면 그 도구만 오류로 내리고 앱은 계속 돈다.
stderr에 쓴 내용은 설정 화면의 오류 문구 뒤에 붙는다. 디버깅할 때 그쪽으로 출력하면 된다.

`tools/adapters/Daiso.Adapter.Claude`가 동작하는 예다.
내장 Claude 파서를 어댑터로 감싼 것이고, 출력이 내장 제공자와 같은지 테스트가 확인한다.
위 YAML도 실제로 실리는지 `ReadmeExampleTests`가 확인한다.
설계 과정과 판단 근거는 [docs/PLUGIN_PLAN.md](docs/PLUGIN_PLAN.md)에 있다.

## 앱이 쓰는 폴더

| 경로 | 내용 |
|---|---|
| `%LOCALAPPDATA%\d-AI-so\settings.json` | 최근 폴더, 단가표, 정리 규칙, 테마, 창 크기 |
| `%LOCALAPPDATA%\d-AI-so\index.db` | 세션 인덱스. 설정이나 `DAISO_INDEX_DB`로 경로를 옮길 수 있다 |
| `%LOCALAPPDATA%\d-AI-so\presets\*.daiso` | 내가 만든 규칙 |
| `%LOCALAPPDATA%\d-AI-so\prompts\` | 내가 만든 프롬프트 |
| `%LOCALAPPDATA%\d-AI-so\profiles\` | 로그인 프로필. DPAPI로 암호화해서 이 PC의 이 계정만 풀 수 있다 |
| `%LOCALAPPDATA%\d-AI-so\logs\crash-*.log` | 예외 기록. 토큰을 가려서 쓰고 최근 20개만 남긴다 |
| `%USERPROFILE%\.daiso\tools\*.yaml` | 직접 추가한 도구 |

인덱스에는 대화 본문이 들어가서 세션이 많으면 커진다.
이 PC에서 세션 243건, 메시지 3만 줄이 95MB다.
C 드라이브가 빠듯하면 설정에서 다른 드라이브로 옮기면 된다.

## 단축키

| 키 | 하는 일 |
|---|---|
| `Ctrl+1` ~ `Ctrl+7` | 왼쪽 메뉴 순서대로 이동 (요약, 사용량, 터미널, 세션, 내 규칙, 내 프롬프트, 설정) |
| `Ctrl+F`, `Esc` | 세션 검색란으로, 검색 지우기 |
| `F5` | 세션 목록 다시 읽기 |
| `Ctrl+S`, `Ctrl+O` | 규칙 저장, 열기 |

같은 표가 설정 화면에도 있다.

## 계정 여러 개 쓰기

요약 화면의 도구 카드에서 지금 로그인을 이름 붙여 보관해 두고, 나중에 그 계정으로 돌아간다.

- 보관: 카드의 `계정`에서 `지금 로그인 저장`
- 전환: 목록에서 `이 계정으로`. 바꾸기 전 상태가 `직전 상태`로 자동 보관되니 한 번 더 누르면 되돌아온다
- 이미 열려 있는 터미널은 그대로다. 새로 여는 터미널부터 바뀐 계정으로 돈다
- 보관한 파일은 DPAPI로 암호화한다. 다른 PC로 복사해도 열리지 않는다
- Antigravity는 로그인이 파일이 아니라 Windows 자격 증명 관리자에 있어서 보관할 수 없다. 카드에 그 이유가 적혀 있다

## 구조

| 프로젝트 | TFM | 역할 |
|---|---|---|
| `src/Daiso.Core` | net8.0 | 모델, 인터페이스, 순수 로직. 파일과 프로세스를 다루지 않는다 |
| `src/Daiso.Providers.Common` | net8.0 | 제공자 공용. 경로 정규화, jsonl 스트리밍, JWT 판독 |
| `src/Daiso.Providers.Claude` | net8.0 | Claude Code 세션과 인증 파서 |
| `src/Daiso.Providers.Codex` | net8.0 | Codex CLI 세션과 인증 파서. 구형과 신형 형식을 모두 읽는다 |
| `src/Daiso.Providers.Antigravity` | net8.0 | Antigravity CLI. 기록은 은퇴한 Gemini CLI가 남긴 것을 읽는다 |
| `src/Daiso.Providers.Manifest` | net8.0 | YAML로 추가한 도구와 어댑터 통신 |
| `src/Daiso.Infrastructure` | net8.0-windows | SQLite 인덱스, 파일 IO, ConPTY 터미널, 휴지통, 로그 마스킹 |
| `src/Daiso.App` | net8.0-windows10.0.19041 | WinUI 3 앱(unpackaged). 화면 7개. 파싱이나 파일 로직은 없다 |
| `tools/Daiso.Cli` | net8.0-windows | 검증용 콘솔 (`daiso`) |
| `tools/adapters/Daiso.Adapter.Claude` | net8.0 | 어댑터 예제. 프로토콜 회귀 테스트용 |
| `tests/*` | | 테스트 세 프로젝트 |

도구를 추가하는 길은 두 가지다. 위에서 본 YAML, 그리고 `IProvider`를 구현해 DI에 등록하는 방법.
화면과 인덱스와 CLI는 둘을 구분하지 않는다. 자세한 내용은 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)에 있다.

## 테스트

```bash
dotnet test Daiso.sln
```

| 프로젝트 | 개수 | 범위 |
|---|---|---|
| `Daiso.Core.Tests` | 362 | 순수 로직과 아키텍처 규칙 14개 |
| `Daiso.Providers.Tests` | 163 | 세 도구의 세션과 인증 파싱, 매니페스트와 어댑터 |
| `Daiso.Infrastructure.Tests` | 118 | 인덱스, PTY(실제 `cmd.exe`를 띄운다), 계정 보관함, 명령 조립 |

아키텍처 테스트는 코드가 아니라 규칙을 검사한다.
Core가 `File`이나 `Process`를 쓰지 못하게, 없는 문구 키를 부르거나 안 쓰는 키를 남기지 못하게,
화면이 `PageBody` 골격과 최소 창 1024를 지키게, 여백과 모서리에 날숫자를 쓰지 못하게,
화면이 뷰모델에 걸어 둔 처리기를 나갈 때 떼게 막는다. UI를 고치는 동안 여러 번 잡혔다.

`tools/shoot-screens.ps1`은 일곱 화면을 여러 창 폭에서 찍는다.
눈으로 훑는 대신 그림을 나란히 놓고 본다. 이 README의 그림도 이 스크립트로 찍었다.

## 하지 않는 것

- 네트워크를 쓰지 않는다(NuGet 복원 제외). 계정 정보와 대화 내용이 이 PC를 떠나지 않는다
- CLI가 만든 파일(`.credentials.json`, `auth.json`, 세션 jsonl)은 읽기만 한다
- 토큰과 API 키 값은 화면, 로그, 예외 메시지에 쓰지 않는다.
  로그인 프로필만 인증 파일을 만지는데 값을 읽지 않고 암호화한 바이트로 옮긴다
- `CLAUDE.md`와 `AGENTS.md`는 `<!-- daiso:start -->` 부터 `<!-- daiso:end -->` 사이만 고친다.
  그 밖은 개행까지 그대로 둔다
- 세션 삭제는 휴지통이 기본이고, 실행 중인 세션은 지우지 않는다
- 텍스트를 읽고 쓸 때 UTF-8을 항상 명시한다

## CLI

앱 없이 확인할 때 쓴다.

```bash
dotnet run --project tools/Daiso.Cli -- auth
```

| 명령 | 하는 일 |
|---|---|
| `daiso auth` | 도구들의 설치와 로그인 상태. 토큰 값은 출력하지 않는다 |
| `daiso auth list \| save \| use \| remove <이름> --tool <id>` | 로그인 프로필 보관, 전환, 삭제 |
| `daiso sessions [--tool <id>] [--include-archived]` | 인덱스의 세션 목록 |
| `daiso search <query>` | 대화 본문 검색. 3글자 이상은 trigram, 2글자는 LIKE |
| `daiso refresh` | 세션 인덱스 갱신 |
| `daiso usage --days N` | 최근 N일 토큰 사용량. 일별, 프로젝트별, 모델별 |
| `daiso rules render \| roundtrip <path>` | `.daiso`를 마크다운으로, 직렬화 안정성 검사 |
| `daiso rules install <projectDir>` | 폴더의 지시문 파일에 daiso 블록 넣기 |
| `daiso rules migrate <projectDir> [--to <id>] [--apply]` | `CLAUDE.md`와 `AGENTS.md` 좌우 비교. `--apply` 없으면 미리보기만 |
| `daiso doctor <dir> [--tool <id>]` | 폴더의 컨텍스트 파일, 글자 수, 중복 줄, 충돌 후보 |
| `daiso export <sessionId> <out.md>` | 세션을 마크다운으로 |

## 문서

| 문서 | 내용 |
|---|---|
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | 무엇을 만드는가 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 계층, 인터페이스, 파싱 규칙의 정본. 코드가 이 문서를 따른다 |
| [docs/GOAL.md](docs/GOAL.md) | 목표와 완료 기준 |
| [docs/PLUGIN_PLAN.md](docs/PLUGIN_PLAN.md) | 도구 추가 기능의 설계와 판단 기록 |
| [docs/UX_SCENARIOS.md](docs/UX_SCENARIOS.md) | 화면 시나리오와 검증 기록 |
| [docs/REVIEW_BACKLOG.md](docs/REVIEW_BACKLOG.md) | 코드 검토에서 나온 지적과 처리 결과 |

## 라이선스

[MIT](LICENSE). 함께 담은 xterm.js와 애드온도 MIT이고 라이선스 전문을 `src/Daiso.App/Assets/xterm/`에 둔다.

Claude Code, Codex CLI, Antigravity CLI는 각 제작사의 상표다.
이 앱은 그 도구들이 PC에 만들어 둔 파일을 읽어 보여 주는 별개의 프로그램이다.

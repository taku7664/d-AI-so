# d-AI-so

AI에 필요한게 다이소.

Claude Code · Codex CLI · Antigravity CLI를 Windows에서 한 창으로 다룬다.
로그인이 살아 있는지, 지난 대화가 어디에 얼마나 쌓였는지, 토큰을 얼마나 썼는지를 보고,
CLI를 앱 안의 방으로 띄운다. 네트워크로 아무것도 보내지 않는다 — 전부 이 PC에 이미 있는 파일을 읽는 것이다.

![요약](docs/screenshots/dashboard.png)

## 무엇이 되나

| | |
|---|---|
| **로그인 상태** | 도구마다 계정·요금제·만료일. 토큰 값은 화면·로그·파일 어디에도 안 쓴다 |
| **터미널** | 도구 → 폴더 → 대화를 고르면 앱 안에서 CLI가 뜬다. 새 창으로 빼도 된다 |
| **세션** | 도구들이 남긴 대화 기록을 프로젝트별로 모아 본문까지 검색하고, 이어서 열거나 markdown으로 뽑는다 |
| **사용량** | 세션 기록에서 계산한 토큰 추이. 단가를 넣으면 비용 추정이 붙는다 |
| **내 규칙** | 조건과 행동을 짜서 `PROJECT_RULES.daiso`를 만들고 `CLAUDE.md`·`AGENTS.md`에 연동한다 |
| **내 프롬프트** | 기획 인터뷰·버그 재현·회고 같은 절차형 프롬프트를 골라 프로젝트에 넣고 첫 메시지로 던진다 |
| **도구 추가** | 앱을 다시 빌드하지 않고 YAML 한 장으로 새 도구를 더한다 ([아래](#도구-직접-추가하기)) |

## 화면

| 터미널 — CLI를 방으로 | 세션 — 찾고 이어서 열기 |
|---|---|
| ![터미널](docs/screenshots/terminal.png) | ![세션](docs/screenshots/sessions.png) |

| 사용량 — 추이와 비용 | 내 규칙 — 트리와 미리보기 |
|---|---|
| ![사용량](docs/screenshots/usage.png) | ![내 규칙](docs/screenshots/rules.png) |

## 시작하기

.NET SDK 8 이상이 필요하다. 이 저장소는 SDK 10에서 `net8.0`을 타겟해 빌드한다(`global.json`).

```bash
dotnet build Daiso.sln
```

경고 0, 오류 0이 정상이다(`TreatWarningsAsErrors=true`). WinUI 앱은 x64로만 빌드되지만
솔루션의 `Any CPU`가 `Daiso.App`은 `x64`로 매핑돼 있어 위 한 줄로 전부 빌드된다.

띄울 때는 이 스크립트를 쓴다. 빌드한 **바로 그 산출물**을 실행한다.

```bash
pwsh tools/run-app.ps1
```

실행 파일은 `src/Daiso.App/bin/Debug/net8.0-windows10.0.19041.0/win-x64/d-AI-so.exe`다.
설치가 필요 없는 unpackaged 앱이고, Windows App SDK를 실행 파일에 넣어(`WindowsAppSDKSelfContained`)
머신에 깔린 런타임 버전에 흔들리지 않는다. 창은 1024×700보다 작아지지 않는다.

## 도구 직접 추가하기

앱이 아는 도구는 세 개로 고정이 아니다. `%USERPROFILE%\.daiso\tools\`에 YAML 한 장을 놓으면
그 도구가 탭·카드·필터에 똑같이 올라온다. **앱을 다시 빌드하지 않는다.**

![도구 플러그인](docs/screenshots/plugins.png)

### 1단계 — 얼굴과 실행

이만큼이면 도구가 보이고, 터미널에서 띄울 수 있다.

```yaml
# %USERPROFILE%\.daiso\tools\mycli.yaml
schema: 1
id: mycli                                  # 소문자·숫자·- 로 2~32자, 글자로 시작. 폴더 이름이 되므로 한 번 정하면 바꾸지 않는다
name: My CLI
short: My                                  # 탭에 쓰는 짧은 이름
vendor: 우리팀
initial: M                                 # 로고 자리의 한 글자
color: "#7A5AF8"                           # 목록으로 두 개 이상 주면 그라데이션
executable: mycli.cmd                      # PATH 에서 찾는다
install:
  uri: https://example.com/mycli           # 안 깔려 있을 때 열어 줄 안내 페이지
  # command: npm install -g mycli          # 대신 설치 명령을 보여 줄 수도 있다
sessionsRoot: "{USERPROFILE}/.mycli/sessions"
rules:
  fileName: MYCLI.md                       # 이 도구가 읽는 프로젝트 지시문 파일
context:
  - "{PROJECT}/MYCLI.md"                   # 컨텍스트 점검이 훑을 자리
resume: "--resume {id}"                    # 이어서 열 때 붙일 인자
```

`{USERPROFILE}` · `{PROJECT}` · `{HERE}`(= 이 YAML이 있는 폴더)를 쓸 수 있다.
더 줄 수 있는 것: `order`(표시 순서), `logoPath`(SVG 경로 데이터), `appendOnly`,
`imagePasteKeys`, `auth.files`(로그인 파일 목록), `models.list`(모델 선택 목록).

**틀리면 조용히 사라지지 않는다.** 모르는 키, 빈 `id`, 내장 도구와 겹치는 `id`,
두 파일이 같은 `id`를 쓰는 경우는 설정 → 도구 플러그인에 이유가 그대로 뜬다.
한 장이 깨져도 나머지는 실린다.

### 2단계 — 지난 대화 읽히기

세션 목록·검색·사용량까지 되게 하려면 기록을 읽어 줄 **어댑터**를 붙인다.

```yaml
adapter:
  command: "node {HERE}/mycli-adapter.js"
```

어댑터는 stdin으로 요청 한 줄을 받고 stdout으로 답을 여러 줄 쓰는 프로그램이다.
줄 하나가 JSON 하나이고, `{"done":true}`로 끝낸다. 언어는 상관없다.

| 들어오는 요청 | 내보낼 답 |
|---|---|
| `{"V":1,"Op":"hello"}` | `{"V":1,"Ok":true,"Name":"...","Done":true}` |
| `{"V":1,"Op":"sessions","Root":"..."}` | `{"Session":{...}}` 여러 줄 + `{"Done":true}` |
| `{"V":1,"Op":"session","FilePath":"..."}` | `{"Session":{...},"Done":true}` |
| `{"V":1,"Op":"messages","FilePath":"...","FromByteOffset":0}` | `{"Message":{...}}` 여러 줄 + `{"ReadTo":1234,"Done":true}` |

키가 대문자로 시작하는 것은 앱이 그렇게 내보내기 때문이다. **읽을 때는 대소문자를 가리지 않으니
답은 `{"session":...}`처럼 써도 된다.** 한글은 이스케이프하지 않아 눈으로 읽힌다.

`Session`은 `Id`·`FilePath`·`ProjectPath`·`StartedAt`·`ModifiedAt`·`SizeBytes`·`UserCount`·`AssistantCount`·`FirstPrompt`·`Usage`,
`Message`는 `At`·`Role`(`user`/`assistant`/`tool`/`system`)·`Text`다. 모르는 `Role`은 `system`으로 본다.

앱은 어댑터를 **오래 사는 프로세스 하나**로 띄워 두고 요청마다 한 줄씩 주고받는다.
죽거나 30초 안에 한 줄도 안 보내면 그 도구만 실패로 내리고 앱은 계속 돈다.
stderr에 쓴 말은 설정 화면의 오류 문구에 꼬리로 붙으니, 디버깅할 때 그쪽으로 찍으면 된다.

동작하는 예가 저장소에 있다. `tools/adapters/Daiso.Adapter.Claude`는 내장 Claude 파서를
어댑터로 감싼 것이고, 그 출력이 내장 제공자와 한 글자도 다르지 않은지 테스트가 지킨다.
위 1단계 YAML도 그대로 실리는지 테스트(`ReadmeExampleTests`)가 확인한다 — 문서의 예제가 안 도는 것이 제일 나쁘다.
자세한 설계와 판정 기록은 [docs/PLUGIN_PLAN.md](docs/PLUGIN_PLAN.md)에 있다.

## 이 앱이 쓰는 폴더

| 경로 | 내용 |
|---|---|
| `%LOCALAPPDATA%\d-AI-so\settings.json` | 최근 폴더·최근 파일, 단가표, 정리 규칙, 테마, 창 크기 |
| `%LOCALAPPDATA%\d-AI-so\index.db` | 세션 인덱스 (설정에서 경로 변경, `DAISO_INDEX_DB`로도 가능) |
| `%LOCALAPPDATA%\d-AI-so\presets\*.daiso` | 내가 만든 규칙 라이브러리 |
| `%LOCALAPPDATA%\d-AI-so\prompts\` | 내가 만든 프롬프트 |
| `%LOCALAPPDATA%\d-AI-so\profiles\` | 로그인 프로필. DPAPI로 암호화해 이 PC의 이 사용자만 풀 수 있다 |
| `%LOCALAPPDATA%\d-AI-so\logs\crash-*.log` | 예외 기록(토큰 가림). 최근 20개만 남는다 |
| `%USERPROFILE%\.daiso\tools\*.yaml` | 직접 더한 도구 |

인덱스는 대화 본문을 담으니 세션이 많으면 커진다. 이 머신에서 세션 243건 · 메시지 3만 줄이
**95MB**다. C: 여유가 빠듯하면 설정에서 다른 드라이브로 옮기면 된다.

## 단축키

| 키 | 하는 일 |
|---|---|
| `Ctrl+1` ~ `Ctrl+7` | 왼쪽 메뉴 순서대로 (요약·사용량·터미널·세션·내 규칙·내 프롬프트·설정) |
| `Ctrl+F` / `Esc` | 세션 검색란으로 / 검색 지우기 |
| `F5` | 세션 목록 다시 읽기 |
| `Ctrl+S` / `Ctrl+O` | 규칙 저장 / 열기 |

앱 안에서는 설정 → 단축키에 같은 표가 있다.

## 계정 여러 개 쓸 때

요약 화면의 도구 카드에서 지금 로그인을 이름 붙여 보관해 두고, 나중에 그 계정으로 되돌린다.

- 보관: 카드의 `계정` → `지금 로그인 저장`
- 전환: 목록에서 `이 계정으로`. 되돌리기 **전에 지금 상태가 `직전 상태`로 자동 보관**되니 한 번 더 누르면 원래대로다
- 이미 열려 있는 터미널은 그대로다. **새로 여는 터미널부터** 바뀐 계정이 적용된다
- 보관한 인증 파일은 DPAPI로 암호화된다. 다른 PC로 복사해도 열리지 않는다
- Antigravity는 로그인이 파일이 아니라 Windows 자격 증명 관리자에 있어 보관할 수 없다. 카드가 그 이유를 적는다

## 구조

| 프로젝트 | TFM | 역할 |
|---|---|---|
| `src/Daiso.Core` | net8.0 | 모델·인터페이스·순수 로직. 파일·경로·프로세스를 안 만진다 |
| `src/Daiso.Providers.Common` | net8.0 | 제공자 공용 (경로 정규화, jsonl 스트리밍, JWT 판독) |
| `src/Daiso.Providers.Claude` | net8.0 | Claude Code 세션·인증 파서 |
| `src/Daiso.Providers.Codex` | net8.0 | Codex CLI 세션·인증 파서 (구형·신형 형식 모두) |
| `src/Daiso.Providers.Antigravity` | net8.0 | Antigravity CLI. 기록은 은퇴한 Gemini CLI가 남긴 것을 계속 읽는다 |
| `src/Daiso.Providers.Manifest` | net8.0 | YAML로 더한 도구 + 바깥 프로세스 어댑터 |
| `src/Daiso.Infrastructure` | net8.0-windows | SQLite 인덱스, 파일 IO, ConPTY 터미널, 휴지통, 로그 가리기 |
| `src/Daiso.App` | net8.0-windows10.0.19041 | WinUI 3 앱(unpackaged). 화면 7개. 파싱·파일 로직은 없다 |
| `tools/Daiso.Cli` | net8.0-windows | 검증용 콘솔 (`daiso`) |
| `tools/adapters/Daiso.Adapter.Claude` | net8.0 | 어댑터 예제 겸 프로토콜 잠금장치 |
| `tests/*` | | 테스트 세 프로젝트 (아래) |

도구를 더하는 길은 둘이다. YAML 한 장(위)이 하나, `IProvider`를 구현해 DI에 등록하는 것이 다른 하나다.
화면·인덱스·CLI는 둘을 구분하지 않는다. 자세한 것은 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## 테스트

```bash
dotnet test Daiso.sln
```

| 프로젝트 | 개수 | 무엇을 |
|---|---|---|
| `Daiso.Core.Tests` | 362 | 순수 로직 + **아키텍처 규칙 14개** |
| `Daiso.Providers.Tests` | 163 | 세 도구의 세션·인증 파싱, 매니페스트·어댑터 |
| `Daiso.Infrastructure.Tests` | 118 | 인덱스, PTY(진짜 `cmd.exe`를 띄운다), 계정 보관함, 명령 조립 |

아키텍처 테스트는 코드가 아니라 **규칙**을 지킨다. Core가 `File`·`Process`를 쓰지 못하게,
없는 문구 키를 부르지도 안 쓰는 키를 남기지도 못하게, 화면이 `PageBody` 골격과 최소 창 1024를
지키게, 여백·모서리에 날숫자를 쓰지 못하게, 화면이 뷰모델에 건 처리기를 떠날 때 떼게 막는다.
UI 작업 중에 실제로 여러 번 잡혔다.

`tools/shoot-screens.ps1`은 일곱 화면을 여러 창 폭에서 찍는다. 눈으로 훑는 대신 그림을 나란히 놓고 본다.
이 README의 그림도 그 스크립트로 찍었다.

## 안 하는 것

- 네트워크 호출이 없다(NuGet 복원 제외). 계정 정보도, 대화 내용도 이 PC를 나가지 않는다
- CLI가 만든 파일(`.credentials.json`, `auth.json`, 세션 jsonl)은 읽기 전용으로 다룬다
- 토큰·API 키 값은 화면·로그·예외 메시지에 쓰지 않는다. 로그인 프로필만 인증 파일을 만지는데,
  값을 읽지 않고 암호화한 바이트로만 옮긴다
- `CLAUDE.md`·`AGENTS.md` 수정은 `<!-- daiso:start -->` ~ `<!-- daiso:end -->` 블록 안에서만 한다. 블록 밖은 개행까지 그대로 둔다
- 세션 삭제는 기본이 휴지통이고, 실행 중인 세션은 거부한다
- 모든 텍스트 IO는 UTF-8을 명시한다

## CLI

앱 없이 확인할 때 쓴다.

```bash
dotnet run --project tools/Daiso.Cli -- auth
```

| 명령 | 하는 일 |
|---|---|
| `daiso auth` | 도구들의 설치·로그인 상태. 토큰 값은 출력하지 않는다 |
| `daiso auth list \| save \| use \| remove <이름> --tool <id>` | 로그인 프로필 보관·전환·삭제 |
| `daiso sessions [--tool <id>] [--include-archived]` | 인덱스의 세션 목록 |
| `daiso search <query>` | 대화 본문 검색 (3글자 이상 trigram, 2글자는 LIKE) |
| `daiso refresh` | 세션 인덱스 갱신 |
| `daiso usage --days N` | 최근 N일 토큰 사용량 (일별·프로젝트별·모델별) |
| `daiso rules render \| roundtrip <path>` | `.daiso`를 마크다운으로 / 직렬화 안정성 검사 |
| `daiso rules install <projectDir>` | 폴더의 지시문 파일에 daiso 마커 블록 넣기·갱신 |
| `daiso rules migrate <projectDir> [--to <id>] [--apply]` | `CLAUDE.md` ↔ `AGENTS.md` 좌우 diff. `--apply` 없이는 미리보기 |
| `daiso doctor <dir> [--tool <id>]` | 폴더의 컨텍스트 파일·글자 수·중복 줄·충돌 후보 |
| `daiso export <sessionId> <out.md>` | 세션을 마크다운으로 |

## 문서

| 문서 | 내용 |
|---|---|
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | 무엇을 만드는가 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 계층·인터페이스·파싱 규칙의 정본. 코드가 이를 따른다 |
| [docs/GOAL.md](docs/GOAL.md) | 목표와 완료 기준 |
| [docs/PLUGIN_PLAN.md](docs/PLUGIN_PLAN.md) | 도구를 더하는 길의 설계와 판정 기록 |
| [docs/UX_SCENARIOS.md](docs/UX_SCENARIOS.md) | 화면 시나리오와 검증 기록 |
| [docs/REVIEW_BACKLOG.md](docs/REVIEW_BACKLOG.md) | 코드 검토에서 나온 것과 그 처리 |

## 라이선스

[MIT](LICENSE). 동봉한 xterm.js와 애드온도 MIT이며 라이선스 전문을 `src/Daiso.App/Assets/xterm/`에 함께 둔다.
Claude Code · Codex CLI · Antigravity CLI는 각 제작사의 상표다.
이 앱은 그 도구들이 이 PC에 만들어 둔 파일을 읽어 보여 주는 별개의 프로그램이다.

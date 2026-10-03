# 도구 플러그인 계획 — 빌드된 앱에 도구를 더할 수 있게

**목표 한 줄**: 앱을 다시 빌드하지 않고, 파일을 놓아 새 AI CLI 를 앱이 알아보게 한다.

**지금은 안 된다.** 어셈블리를 동적으로 읽는 곳이 없고(`Assembly.Load` · `AssemblyLoadContext` 를 쓰는 코드가 없다),
도구를 하나 더하려면 `ToolKind` enum · `ToolLook` · `ToolIcon` · XAML 탭 · DI 등록을 손으로 고쳐 다시 빌드해야 한다.

> **2026-09-11 재검수에서 1차 초안을 한 군데 뒤집었다.** "세션 로그를 선언형 필드 매핑으로 읽는다"는
> 실제 파서를 열어 보니 성립하지 않는다(§4). 그 자리를 **바깥 프로세스 어댑터**로 바꿨다(§6).
>
> **2026-09-11 Stage 0~6 구현 완료.** 아래 단계는 전부 들어갔다. 실제로 놓인 곳:
> `src/Daiso.Core/Plugins/` (매니페스트·파서) · `src/Daiso.Providers.Manifest/` (도구·로더·어댑터 통로) ·
> `src/Daiso.App/Services/ToolPluginCatalog.cs` (목록과 실패) · 설정 화면의 `도구 플러그인` 카드 ·
> `tools/adapters/Daiso.Adapter.Claude/` (참조 어댑터).
> 구현하며 계획과 달라진 것은 각 단계 끝에 **실제**로 적어 뒀다.

---

## 1. 지금 무엇이 막고 있나

| 막는 것 | 어디 | 왜 막히나 |
|---|---|---|
| **`ToolKind` 가 닫힌 enum** | `src/Daiso.Core/ToolKind.cs` | 바깥에서 enum 값을 만들 수 없다 |
| **표시 정보가 코드에 박혀 있다** | `Daiso.App/Services/ToolLook.cs` (도구별 switch 20곳) · `Daiso.App/Controls/ToolIcon.cs` | 이름 · 제작사 · 한 글자 · 색 · 그라데이션 · 로고 경로 · 표시 순서가 전부 `switch (kind)` |
| **XAML 에 도구 이름이 박혀 있다** | `DashboardPage` · `SessionsPage` · `UsagePage` 의 `SelectorBarItem` | 도구 탭 셋이 하드코딩. 새 터미널 카드만 `ToolLook.InDisplayOrder` 로 돈다 |

**`ToolKind` 이름이 디스크에 두 군데 적힌다** — 1차 초안은 하나만 봤다.

| 어디 | 어떻게 |
|---|---|
| SQLite 인덱스 `sessions.tool` 열 | `SqliteSessionIndex` 의 `Enum.Parse<ToolKind>(reader.GetString(0))`. `IndexFormatVersion` = 4 |
| 계정 보관함 | `AuthProfileStore` 의 `Enum.TryParse<ToolKind>(row.Tool, …)` **그리고 `DirectoryFor(ToolKind, name)` — 폴더 이름이다** |

둘 다 `Enum.Parse` 계열이라 **모르는 값을 만나면 지금은 던지거나 버린다.** 플러그인이 생기면 모르는 id 가 남는 것이 정상 상황이다.

**잘 돼 있는 것**: `IProvider` 는 이미 제대로 된 어댑터 경계이고, DI 가 `IEnumerable<IProvider>` 로 주입한다.
인덱스 · 내보내기 · 지시문 맞추기는 이미 도구를 모르고 돈다. **핵심 로직은 손댈 것이 거의 없다.**

---

## 2. Core 순수성이 정하는 제약

`CorePurityTests` 가 `Daiso.Core` 에서 `File` · `Directory` · `Process` · `Environment` 참조를 금지한다(ARCHITECTURE §1).
플러그인 설계가 여기에 걸린다:

- **매니페스트를 읽는 코드는 Core 바깥이다.** Core 는 파일을 못 연다.
  *실제로는 `Daiso.Providers.Manifest` 에 넣었다* — 실행 파일 찾기(`ExecutableLocator`)·홈 폴더(`ProviderHome`)가
  `Daiso.Providers.Common` 에 있어서, 다른 도구 어댑터들과 같은 자리에 두는 편이 맞았다.
- **표시 정보는 Core 에서 문자열이다.** 색은 `#RRGGBB`, 로고는 24×24 SVG path 문자열.
  `Windows.UI.Color` · `Brush` 로 바꾸는 일은 `Daiso.App` 이 한다 — 지금 `ToolLook` 이 하는 일이 그대로 남는다.
  (로고가 이미 SVG path 문자열인 것이 여기서 크게 유리하다. 이미지 파일을 따로 다룰 필요가 없다.)
- **바깥 프로세스를 띄우는 것도 Infrastructure 다.** `Daiso.Providers.Common/CommandRunner.cs` 가 이미 있다.

---

## 3. 무엇이 선언으로 되고 무엇이 안 되나

**선언으로 되는 것** — 계약의 대부분이 그냥 데이터다.

| 항목 | 계약 |
|---|---|
| 실행 파일 · 설치 명령 · 설치 안내 주소 | `ExecutableName` · `InstallCommand` · `InstallUri` |
| 세션 폴더 | `SessionsRoot` |
| 규칙 파일 이름 · import 지원 여부 | `RulesFileName` · `SupportsInstructionImports` |
| 컨텍스트 파일 경로 | `ContextFilePatterns` |
| 이어서 열기 인자 꼴 (`--resume {id}`) | `BuildResumeArguments` |
| 그림 붙여넣기 키 | `ImagePasteKeys` |
| 로그인 파일 목록 · 파일에 사는가 | `AuthFiles` · `LoginLivesInFiles` |
| 모델 목록 | `ListModelsAsync` — 명령 한 줄 / 캐시 파일 / 고정 목록 |
| 표시: 이름 · 제작사 · 짧은 이름 · 한 글자 · 색 · 로고 | 지금 `ToolLook` 이 들고 있는 것 |

**선언으로 안 되는 것** — 셋뿐이지만 이 셋이 세션 화면 전부다.

`EnumerateSessionsAsync` · `ReadSessionInfoAsync` · `ReadMessagesAsync`.

---

## 4. 왜 안 되는지 — 실제 파서를 열어 본 결과

1차 초안은 "세 도구가 다 JSONL 이고 공용 `JsonlReader` 를 쓰니, 도구별 차이는 필드 이름 매핑이다"라고 썼다.
**틀렸다.** `*Record.cs` 셋을 열어 보면 차이는 이름이 아니라 **모양과 상태**다.

| 도구 | 선언형이 못 넘는 벽 | 근거 |
|---|---|---|
| **Claude** | `message.content` 가 **문자열이거나 블록 배열**이고, 배열이면 text·tool_use·tool_result 를 갈라 읽는다. **레코드 하나가 메시지 여럿**을 내고 역할도 서로 다르다(User·Tool·Assistant). `toolUseResult` 대체 경로, `isSidechain` | `ClaudeRecord.cs` `UserMessages` · `Assistant` |
| **Codex** | `payload.type` → `item.type` → `role` **3단 분기** | `CodexRecord.cs` 73 · 103 · 118 · 140행 |
| **Antigravity** | `$rewindTo` = 그 id 부터 끝까지 **잘라낸다**. `$set.messages` = 목록을 **통째로 갈아 끼운다**. 즉 **상태를 들고 읽어야 한다** (그래서 `AppendOnlySessions = false`) | `GeminiRecord.cs` 37 · 61 · 67행 |

점 표기 필드 매핑은 이 셋 중 **하나도** 표현하지 못한다. 한 레코드 → 여러 메시지, 타입 분기, 되감기 상태는 값 경로가 아니라 **계산**이다.

**결론**: 1차 초안의 Stage 5 는 자기 완료 기준("Claude 재현")을 통과할 수 없었다.
매핑 문법을 이걸 다 담을 만큼 키우면 그건 이미 프로그래밍 언어다 — 그 순간 데이터가 아니라 검사할 수 없는 코드가 된다.

---

## 5. 갈래 넷

| | 얼개 | 세션 읽기 | 위험 |
|---|---|---|---|
| **A. 선언형만** | 매니페스트 하나 | ❌ 불가 (§4) | 낮다 |
| **B. 어셈블리** | `tools/*.dll` 을 `AssemblyLoadContext` 로 | ✅ | **앱 프로세스 안에서** 서명 없는 코드 실행. `Daiso.Core` 버전 결합. 언로드·크래시가 앱을 죽인다 |
| **C. 바깥 프로세스 어댑터** | 플러그인이 **stdio 로 JSON 을 주고받는 실행 파일**. LSP · MCP 가 같은 문제를 푼 방식 | ✅ | 코드를 실행하는 건 같지만 **프로세스가 갈려 있다** — 죽여도 앱이 산다. 언어 자유. 버전 결합 없음 |
| **D. A + C 하이브리드** | 데이터로 되는 건 매니페스트, 세션 읽기만 어댑터(있으면) | ✅ (어댑터를 붙였을 때) | C 와 같되, **어댑터 없이도 도구가 반쯤 쓸모 있다** |

### 고른 것: **D**

- **A 만으로는 반쪽이다.** 세션 목록 · 사용량 · 이어서 열기가 통째로 빈다. 이 앱의 절반이 세션 화면이다.
- **B 보다 C 다.** 결정적인 차이는 "코드를 실행하느냐"가 아니라 **어디서 실행하느냐**다.
  이 앱은 이미 CLI 를 프로세스로 띄운다(`CommandRunner`) — C 는 새로운 위험 종류가 아니다.
  반면 B 는 남의 DLL 을 **앱 프로세스 안에** 올린다. 크래시 하나가 앱을 죽이고, `Daiso.Core` 를 바꿀 때마다 플러그인이 깨진다.
- **D 의 단계성이 실무에서 이긴다.** 매니페스트만 놓으면 그날로 *실행 · 설치 · 규칙 · 프롬프트 · 카드 · 탭* 이 된다.
  세션 기록이 필요하면 그때 어댑터를 붙인다. 플러그인 작성자가 한 번에 다 하지 않아도 된다.

**성능 걱정은 없다.** 어댑터는 파일마다 띄우는 게 아니라 **오래 사는 프로세스 하나**이고,
요청·응답은 줄 단위 JSON 이다. `ReadMessagesAsync` 가 이미 `IAsyncEnumerable` 이라 스트리밍에 그대로 맞는다.

---

## 6. 어댑터 프로토콜 (초안)

stdio, 줄 단위 JSON. 요청 하나에 응답 여러 줄이 올 수 있고 `"done": true` 로 끝난다.

```
→ {"v":1,"op":"hello"}
← {"v":1,"ok":true,"name":"my-cli-adapter","appendOnly":true,"done":true}

→ {"v":1,"op":"sessions","root":"C:/Users/me/.mycli/sessions"}
← {"v":1,"session":{"id":"…","filePath":"…","projectPath":"…","startedAt":"…","modifiedAt":"…",
                    "sizeBytes":1234,"userCount":3,"assistantCount":3,"firstPrompt":"…",
                    "usage":{"input":0,"output":0,"cacheCreate":0,"cacheRead":0},"toolVersion":"…"}}
← {"v":1,"done":true}

→ {"v":1,"op":"messages","filePath":"…","fromByteOffset":0}
← {"v":1,"message":{"at":"…","role":"user","text":"…","isSidechain":false}}
← {"v":1,"done":true,"readTo":8192}
```

**정한 것**

- `v` 는 **프로토콜 판**이다. `hello` 에서 맞춰 보고, 앱이 모르는 판이면 그 도구만 오류로 표시하고 나머지는 산다.
  이게 없으면 계약을 한 번 바꿀 때마다 모든 플러그인이 조용히 깨진다.
- 모양은 `SessionInfo` · `SessionMessage` 를 그대로 옮긴 것이다(`src/Daiso.Core/Models/SessionInfo.cs`).
  **필드를 새로 상상하지 않는다** — 지금 화면이 쓰는 것만 넘긴다.
- `fromByteOffset` · `readTo` 로 이어 읽기를 지원한다. `appendOnly:false` 면 앱이 오프셋을 안 준다(지금 규칙과 같다).
- 어댑터가 죽거나 규정을 어기면 **그 도구만** 오류다. 앱은 뜬다.

---

## 7. 매니페스트 (초안)

```yaml
# %USERPROFILE%\.daiso\tools\mycli.yaml
schema: 1
id: mycli                     # 인덱스와 계정 보관함 폴더 이름에 쓰인다. 바꾸지 않는다 (§8)
name: My CLI
short: My
vendor: Someone
initial: M
color: "#4E86F7"
logoPath: "M12 3.6 2.4 20.4h4.2L12 10.8l5.4 9.6h4.2L12 3.6Z"   # 24x24 SVG path (선택)

executable: mycli.cmd
install:
  command: npm install -g my-cli      # 또는
  uri: https://example.com/install    # 이것이 있으면 명령을 돌리지 않고 페이지를 연다

sessionsRoot: "{USERPROFILE}/.mycli/sessions"
appendOnly: true

rules:
  fileName: MYCLI.md
  supportsImports: false
context:
  - "{PROJECT}/MYCLI.md"
  - "{USERPROFILE}/.mycli/MYCLI.md"

resume: "--resume {id}"
loginArguments: "login"          # 로그인 절차를 띄우는 인자. 비우면 도구를 그냥 띄운다
firstPromptFlag: ""              # 첫 프롬프트 앞 깃발. Antigravity 의 `-i` 같은 것. 비우면 메시지만 넘긴다
imagePasteKeys: ""               # Ctrl+V

auth:
  livesInFiles: true
  files:
    - path: "{USERPROFILE}/.mycli/auth.json"
      required: true

models:                          # 고정 목록만 받는다 (아래 설명)
  list:
    - id: fast
      name: 빠른 모델

adapter:                         # 없으면 세션 기록 없이 동작한다 (§5 D)
  command: "{HERE}/mycli-adapter.exe"
```

치환은 `{USERPROFILE}` · `{PROJECT}` · `{id}` · `{HERE}`(매니페스트가 있는 폴더) **넷뿐이다.**
표현식 · 조건 · 반복을 넣지 않는다 — 넣고 싶어지는 순간이 어댑터로 갈 신호다.

**모델은 고정 목록만 받는다.** 1차 초안에는 `from: command` 로 명령을 돌려 목록을 얻는 길이 있었는데 뺐다 —
매니페스트를 *읽는 것만으로* 남의 명령이 돌아가면 안 된다. 파일을 놓는 것과 코드를 돌리는 것은 다른 결정이고,
후자는 어댑터라는 눈에 보이는 자리에서만 일어나야 한다(설정 화면이 그 명령을 그대로 보여 준다).

---

## 8. id 규칙 — 한 번 정하면 못 바꾼다

`id` 는 **SQLite 인덱스의 `tool` 열**과 **계정 보관함 폴더 이름**에 그대로 적힌다(§1).

- 소문자 · 숫자 · `-` 만. 길이 2~32. **파일 이름으로 안전해야 한다** (폴더 이름이 되므로)
- 내장 셋(`claude` · `codex` · `antigravity`)은 예약어다
- **같은 id 가 둘이면 둘 다 안 싣고 오류로 표시한다.** 먼저 읽은 것이 이기게 하면 파일 순서에 따라 앱이 달라진다
- 사람이 id 를 바꾸면 그 도구의 지난 세션·계정은 **다른 도구의 것이 된다.** 매니페스트 주석과 설정 화면에 적는다

---

## 9. 단계

되돌릴 수 있게 자른다. **Stage 1 만 되돌리기 어렵다.**

### Stage 0 — 지금 동작을 고정한다
세 도구가 실제 세션 표본에서 만들어 내는 `SessionInfo` · `SessionMessage` 를 특성화 테스트로 못 박는다.
뒤 단계 전부가 "이 값이 안 바뀌는가"로 검사된다.
**완료 기준**: 도구 셋 각각 고정 표본 → 기대값 비교 테스트가 있다.
**실제**: `tests/Daiso.Providers.Tests/Common/SessionShapeTests.cs` + `Common/Snapshots/*.txt`.
세션 메타 · 본문을 훑은 메타 · 메시지 전부를 한 장의 글로 만들어 견준다. 파일 경로·크기는 뺀다(임시 폴더마다 달라진다).

### Stage 1 — `ToolKind` enum → 문자열 id ⚠️ 되돌리기 어려움
`ToolId`(문자열을 감싼 record)로 바꾼다. 내장 셋은 상수로 남긴다.
**두 저장소를 같이 옮긴다**: `SqliteSessionIndex`(`IndexFormatVersion` 4 → 5) · `AuthProfileStore`(폴더 이름 포함).
**모르는 id 를 만나도 죽지 않는다** — 지금 `Enum.Parse` 는 던진다.
**완료 기준**: 인덱스를 다시 지어 세션 수가 전과 같다. 보관해 둔 계정이 그대로 보이고 되돌리기가 된다. Stage 0 이 통과한다.
**실제**: `ToolKind` 를 `readonly record struct` 로 바꿔 **이름을 지켰다** — 부르는 곳 223 군데를 안 건드렸다.
`switch (kind)` 는 상수가 아니게 되어 `switch (kind.Id)` 로 바꿨다(다섯 곳).
옛 기록의 `"Claude"` 를 읽어야 해서 `TryParse` 는 대소문자를 안 가린다. 계정 보관함 폴더도 Windows 가
대소문자를 안 가려 그대로 찾힌다. **스냅샷에서 바뀐 줄은 파일마다 `tool=` 한 줄뿐이었다.**

### Stage 2 — 표시 정보를 `IProvider` 로
이름 · 제작사 · 짧은 이름 · 한 글자 · **색(`#RRGGBB` 문자열)** · **로고(SVG path 문자열)** · 표시 순서를 `IProvider` 가 낸다.
`ToolLook` 은 지우지 않고 **조회 창구로 남긴다** — 부르는 곳 20군데를 안 건드리고 안쪽만 바꾼다.
문자열 → `Color` · `Brush` 변환은 `ToolLook` 이 계속 한다(§2).
**완료 기준**: `ToolLook` 에 `switch (kind)` 가 없다. `CorePurityTests` 가 통과한다. 화면이 전과 같이 보인다.
**실제**: `ToolDisplay`(Core, 문자열만) 를 `IProvider.Display` 로 받는다. 기본 구현을 두지 않아 도구를 더하면
컴파일러가 이름·색·로고를 묻는다. 단색/그라데이션을 따로 묻지 않고 **색 개수가 곧 답**이다.
`ToolLook` 은 값 없는 조회 창구로 남아 부르는 곳 스무 군데가 그대로다.
소스 글자를 훑던 `ToolLookTests` 는 값을 보는 `ToolDisplayTests` 로 바꿨다 — 검사할 것이 글자가 아니라 동작이다.

### Stage 3 — XAML 탭을 데이터로
`SelectorBarItem` 셋을 `ItemsSource` 로 바꾼다.
**완료 기준**: XAML 에 도구 이름 문자열이 없다. 테스트가 잠근다.
**실제**: `SelectorBar` 에는 `ItemsSource` 가 없어 `SelectorBarVisuals.FillToolTabs` 가 코드로 채운다.
`ToolNameInXamlTests` 가 화면 XAML 전부를 훑어 도구 이름이 다시 박히는 것을 막는다(주석은 뺀다).

### Stage 4 — 매니페스트 로더 (어댑터 없이)
`%USERPROFILE%\.daiso\tools\*.yaml` → `ManifestProvider : IProvider`. 로더는 `Daiso.Infrastructure` 에 둔다(§2).
세션 읽기는 빈 목록을 낸다.
**완료 기준**: 매니페스트 하나를 놓고 앱을 켜면 도구가 넷이 되고, **실행 · 설치 · 규칙 · 프롬프트 · 카드 · 탭이 다 된다.**
매니페스트 하나가 깨져도 앱이 뜨고 나머지 도구가 산다.
**실제**: 플러그인은 **컨테이너를 세우기 전에** 읽는다. `IEnumerable<IProvider>` 를 직접 등록해 합치려 하면
그 팩터리 안의 `GetServices<IProvider>()` 가 자기 자신을 불러 끝없이 돈다 — 읽어서 하나씩 `IProvider` 로
등록하면 컨테이너의 기본 동작이 그대로 산다. 완료 기준은 `ToolPluginLoaderTests` 11 건이 잠근다.

### Stage 5 — 어댑터 프로토콜
§6 을 구현한다. **검증은 Claude 재현**: `ClaudeProvider` 를 감싼 시험용 어댑터를 만들어,
매니페스트 + 어댑터로만 Stage 0 의 기대값과 같은 값이 나오는지 본다.
이 시험용 어댑터는 **참조 구현으로 저장소에 남긴다** — 플러그인 작성자가 베낄 것이 있어야 한다.
**완료 기준**: 위 재현이 통과한다. 어댑터를 강제 종료해도 앱이 살고 그 도구만 오류가 된다.
**실제**: `AdapterReproducesClaudeTests` 가 **진짜 프로세스를 띄워** 내장 `ClaudeProvider` 와 값을 견준다
(세션 · 훑은 메타 · 메시지의 역할·사이드체인·시각까지). 흉내 낸 프로세스로는 잴 의미가 없어서 그렇게 했다.
`AdapterFailureTests` 가 못 띄우는 어댑터 · 말없이 끝나는 어댑터 · JSON 이 아닌 줄을 뱉는 어댑터를 본다 —
셋 다 목록이 비고 이유만 남는다. 한 요청에 30 초 제한을 두어 멈춘 어댑터가 화면을 붙잡지 못한다.

### Stage 6 — 사람에게 보이게
설정에 `도구 플러그인` 목록: 어디서 읽었는지 · 무엇이 틀렸는지 · **어떤 명령을 돌리는지** · 다시 읽기.
**완료 기준**: 깨진 매니페스트, id 충돌, 죽는 어댑터 — 셋 다 앱이 뜨고 그 자리에서 이유를 말한다.
**실제**: 설정 화면의 `도구 플러그인` 카드. 성공한 것과 실패한 것이 **같은 목록**에 있고,
각 줄이 파일 경로 · 상태(`지난 대화까지 읽습니다` / `실행은 되지만 지난 대화는 못 읽습니다`) ·
**돌리는 명령** · 틀린 이유를 그대로 보여 준다. `다시 읽기` 는 목록만 새로 읽으므로
**앱을 다시 켜야 한다고 말한다** — 조용히 안 늘면 "왜 안 되지"가 된다.

---

## 10. 위험과 되돌리기

| 위험 | 대응 |
|---|---|
| **인덱스·계정 보관함 재생성**(Stage 1) | 유일하게 되돌리기 어려운 단계. Stage 0 을 먼저 둔다. 인덱스 재생성은 이미 해 본 길이다(포맷 4) |
| **모르는 도구 id 가 저장소에 남는다** | 옛 행을 만나도 죽지 않고 "모르는 도구"로 흘려보낸다. Stage 1 에서 두 저장소 모두 고친다 |
| **깨진 매니페스트가 앱을 못 뜨게 한다** | 로더는 하나가 실패해도 나머지를 싣는다. 실패는 Stage 6 화면에 모인다 |
| **어댑터가 죽거나 멈춘다** | 시간 제한을 두고 그 도구만 오류로 내린다. 앱 · 다른 도구는 산다. Stage 5 완료 기준에 강제 종료 시험을 넣는다 |
| **어댑터가 임의 코드다** | 사람이 그 파일을 놓은 것이고 앱은 이미 CLI 를 띄운다. 대신 **무엇을 돌리는지 설정 화면에 그대로 보인다**(Stage 6) |
| **매니페스트가 스크립트가 된다** | 치환 넷뿐. 표현식이 필요해지면 어댑터로 간다 |
| **프로토콜을 바꾸면 플러그인이 깨진다** | `v` 로 판을 맞춘다(§6). 모르는 판은 그 도구만 오류 |
| **표시 정보를 옮기다 화면이 미묘하게 바뀐다** | Stage 2 완료 기준을 "전과 같이 보인다"로 두고 `ToolLook` 을 창구로 남긴다 |

**되돌리기**: Stage 2~6 은 서로 독립이라 개별 되돌리기가 된다. Stage 1 은 그 위가 다 얹히므로 완료 기준 전에 넘어가지 않는다.

---

## 11. 안 하기로 한 것

- **세션 로그 선언형 매핑** — §4. 실제 파서 셋 중 하나도 표현 못 한다. 담으려 키우면 그건 언어다.
- **어셈블리(in-proc) 플러그인** — §5 B. C 가 같은 일을 하면서 앱을 안 죽이고 버전 결합도 없다.
  C 로 못 푸는 구체적인 요구가 나오면 그때 다시 본다.
- **매니페스트에 스크립트 · 표현식** — 검사할 수 없는 것을 데이터라고 부르지 않는다.
- **플러그인 장터 · 원격 설치** — 앱이 남의 파일을 받아 오지 않는다. 사람이 폴더에 놓는다.
- **`ToolKind` 를 남긴 채 "기타" 값 하나로 때우기** — 인덱스·화면이 도구를 못 가른다. 결국 다시 뜯는다.

# 도구 플러그인 계획 — 빌드된 앱에 도구를 더할 수 있게

**목표 한 줄**: 앱을 다시 빌드하지 않고, 파일 하나를 놓아 새 AI CLI 를 앱이 알아보게 한다.

**지금은 안 된다.** 어셈블리를 동적으로 읽는 곳이 없고(`Assembly.Load` · `AssemblyLoadContext` 를 쓰는 코드가 없다),
도구를 하나 더하려면 `ToolKind` enum · `ToolLook` · `ToolIcon` · XAML 탭 · DI 등록을 손으로 고쳐 다시 빌드해야 한다.

---

## 1. 지금 무엇이 막고 있나

세 가지다. 로더를 먼저 붙이는 것이 아니라 이 셋을 걷어내는 것이 일의 대부분이다.

| 막는 것 | 어디 | 왜 막히나 |
|---|---|---|
| **`ToolKind` 가 닫힌 enum** | `src/Daiso.Core/ToolKind.cs` | 바깥에서 enum 값을 만들 수 없다. 게다가 이 값의 **이름이 SQLite 인덱스에 문자열로 저장**된다 (`SqliteSessionIndex` 의 `Enum.Parse<ToolKind>`) |
| **표시 정보가 코드에 박혀 있다** | `src/Daiso.App/Services/ToolLook.cs` (도구별 switch 20곳) · `src/Daiso.App/Controls/ToolIcon.cs` | 이름 · 제작사 · 한 글자 · 색 · 그라데이션 · 로고 · 표시 순서가 전부 `switch (kind)` 다 |
| **XAML 에 도구 이름이 박혀 있다** | `DashboardPage.xaml` · `SessionsPage.xaml` · `UsagePage.xaml` 의 `SelectorBarItem` | 도구 탭 셋이 하드코딩. 새 터미널 카드만 `ToolLook.InDisplayOrder` 로 돈다 |

**잘 돼 있는 것도 있다.** `IProvider` 는 이미 제대로 된 어댑터 경계이고, DI 가 `IEnumerable<IProvider>` 로 주입한다.
인덱스 · 내보내기 · 지시문 맞추기 같은 서비스는 이미 도구를 모르고 돈다. **핵심 로직은 손댈 것이 거의 없다.**

---

## 2. 무엇이 선언으로 되고 무엇이 코드인가

`IProvider` 계약을 뜯어 보면 깔끔하게 갈린다.

**선언으로 되는 것** (거의 전부)

| 항목 | 계약 |
|---|---|
| 실행 파일 · 설치 명령 · 설치 안내 주소 | `ExecutableName` · `InstallCommand` · `InstallUri` |
| 세션 폴더 | `SessionsRoot` |
| 규칙 파일 이름 (`CLAUDE.md` 꼴) · import 지원 여부 | `RulesFileName` · `SupportsInstructionImports` |
| 컨텍스트 파일 경로 | `ContextFilePatterns` |
| 이어서 열기 인자 꼴 (`--resume {id}`) | `BuildResumeArguments` |
| 그림 붙여넣기 키 | `ImagePasteKeys` |
| 로그인 파일 목록 · 파일에 사는가 | `AuthFiles` · `LoginLivesInFiles` |
| 모델 목록 얻는 법 (명령 한 줄 / 캐시 파일 / 고정 목록) | `ListModelsAsync` |
| **표시**: 이름 · 제작사 · 색 · 로고 | 지금은 `ToolLook`. 로고가 이미 **SVG 경로 문자열**이라 매니페스트에 글로 담을 수 있다 |

**코드가 필요한 것** (셋뿐)

`EnumerateSessionsAsync` · `ReadSessionInfoAsync` · `ReadMessagesAsync` — CLI 마다 세션 로그 스키마가 다르다.

다만 지금 세 도구가 **모두 JSONL** 이고, 이미 공용 `JsonlReader` · `JsonHelpers` 를 쓴다.
도구별로 다른 것은 `ClaudeRecord` · `CodexRecord` · `GeminiRecord` (각 213~251줄) 의 **필드 이름 매핑**이 대부분이다.
그래서 **JSONL + 필드 매핑**까지는 선언으로 덮을 수 있다 (§5).

---

## 3. 두 갈래와 고른 것

| | 얼개 | 되는 범위 | 위험 |
|---|---|---|---|
| **A. 선언형 매니페스트** | `%USERPROFILE%\.daiso\tools\*.yaml` 를 읽어 `ManifestProvider : IProvider` 를 만든다 | JSONL 로그를 쓰는 CLI | 낮다. **남의 코드를 실행하지 않는다** |
| **B. 어셈블리 플러그인** | `tools\*.dll` 을 `AssemblyLoadContext` 로 읽어 `IProvider` 구현을 긁는다 | 전부 | 임의 코드 실행 · 서명 없는 DLL · `Daiso.Core` 버전 결합 |

**A 를 기본으로 간다. B 는 A 가 다 끝난 뒤에 옵션으로 검토한다.**

이 앱이 대상으로 삼는 사람은 *AI CLI 를 처음 써 보는 일반인*이다(`TERMINAL_CARD_PLAN.md`).
그 사람에게 "DLL 을 폴더에 넣으세요"라고 할 수 없고, 앱이 서명 없는 남의 코드를 로드하는 것도 옳지 않다.
A 만으로도 "새 CLI 나왔는데 왜 지원 안 하냐"의 대부분이 풀린다. **A 는 B 로 가는 길을 막지 않는다** —
로더가 `IProvider` 목록을 만들어 DI 에 넣는 자리는 같다.

---

## 4. 매니페스트 (초안)

```yaml
# %USERPROFILE%\.daiso\tools\mycli.yaml
id: mycli                     # 인덱스에 저장되는 값. 한 번 정하면 바꾸지 않는다
name: My CLI                  # 카드 제목
short: My                     # 버튼·미리보기용 짧은 이름
vendor: Someone               # 제작사
initial: M                    # 배지 한 글자
color: "#4E86F7"              # 배지 색
logoPath: "M12 3.6 2.4 20.4h4.2L12 10.8l5.4 9.6h4.2L12 3.6Z"   # 24x24 SVG path (선택)

executable: mycli.cmd
install:
  command: npm install -g my-cli      # 또는
  uri: https://example.com/install    # 이것이 있으면 명령을 돌리지 않고 페이지를 연다

sessionsRoot: "{USERPROFILE}/.mycli/sessions"
appendOnly: true                       # 뒤에만 붙는 로그인가

rules:
  fileName: MYCLI.md
  supportsImports: false
context:
  - "{PROJECT}/MYCLI.md"
  - "{USERPROFILE}/.mycli/MYCLI.md"

resume: "--resume {id}"
imagePasteKeys: ""               # Ctrl+V

auth:
  livesInFiles: true
  files:
    - path: "{USERPROFILE}/.mycli/auth.json"
      required: true

models:
  from: command                        # command | file | list
  command: "mycli models --json"
  idField: id
  nameField: name

session:                               # §5
  format: jsonl
  ...
```

`{USERPROFILE}` · `{PROJECT}` · `{id}` 만 치환한다. **임의 표현식은 넣지 않는다** — 매니페스트는 데이터이지 스크립트가 아니다.

---

## 5. 세션 로그 필드 매핑

가장 어려운 부분이자, 여기서 선언형의 한계가 정해진다.

```yaml
session:
  format: jsonl
  idFrom: fileName                # fileName | field
  projectPath: cwd                # 프로젝트 경로가 든 키
  timestamp: timestamp
  role:
    field: type
    user: user
    assistant: assistant
  text: message.content           # 점 표기로 중첩 접근
  usage:
    input: message.usage.input_tokens
    output: message.usage.output_tokens
    cacheCreate: message.usage.cache_creation_input_tokens
    cacheRead: message.usage.cache_read_input_tokens
  version: version                # 선택
```

**완료 기준이 곧 검증이다**: 이 매핑으로 `ClaudeRecord` 를 재현할 수 있어야 한다.
Stage 5 는 *Claude 를 매니페스트로 기술해 기존 `ClaudeProvider` 와 같은 `SessionInfo` 를 내놓는지* 로 검사한다.
재현이 안 되면 매핑이 부족한 것이므로 그때 필요한 만큼만 늘린다 — **미리 상상해서 키를 늘리지 않는다.**

못 덮는 것은 처음부터 적어 둔다: 목록 교체·되감기 레코드가 있는 로그(Antigravity 가 그래서 `AppendOnlySessions = false` 다),
JSONL 이 아닌 형식, 여러 파일에 흩어진 한 세션. 이런 도구는 B(어셈블리)로 간다.

---

## 6. 단계

되돌릴 수 있게 자른다. **Stage 1 만 되돌리기 어렵다**(인덱스를 다시 짓는다).

### Stage 0 — 지금 동작을 고정한다
세 도구가 실제 세션 파일에서 만들어 내는 `SessionInfo` · `SessionMessage` 를 특성화 테스트로 못 박는다.
뒤 단계가 전부 이 값을 안 바꾸는지로 검사된다.
**완료 기준**: 도구 셋 각각 고정 표본 파일 → 기대 `SessionInfo` 비교 테스트가 있다.

### Stage 1 — `ToolKind` enum → 문자열 id ⚠️ 되돌리기 어려움
`ToolKind` 를 `ToolId`(문자열 감싼 record)로 바꾼다. 내장 셋은 상수로 남긴다(`ToolId.Claude` 등).
`SqliteSessionIndex.IndexFormatVersion` 4 → 5. `Enum.Parse<ToolKind>` 자리가 문자열 그대로가 된다.
**모르는 id 를 만나도 죽지 않아야 한다** — 지금은 `Enum.Parse` 가 던진다.
**완료 기준**: 인덱스를 지우고 다시 지어 세션 수가 전과 같다. Stage 0 테스트가 그대로 통과한다.

### Stage 2 — 표시 정보를 `IProvider` 로
이름 · 제작사 · 짧은 이름 · 한 글자 · 색 · 로고 경로 · 표시 순서를 `IProvider` 가 내놓는다.
`ToolLook` 은 **지우지 않고 조회 창구로 남긴다** — 부르는 곳 20군데를 그대로 두고 안쪽만 바꾼다.
**완료 기준**: `ToolLook` 에 `switch (kind)` 가 하나도 없다. 화면이 전과 같이 보인다.

### Stage 3 — XAML 탭을 데이터로
`DashboardPage` · `SessionsPage` · `UsagePage` 의 `SelectorBarItem` 셋을 `ItemsSource` 로 바꾼다.
**완료 기준**: XAML 에 도구 이름 문자열이 없다. 테스트가 잠근다(`PageSkeletonTests` 옆에 한 건 추가).

### Stage 4 — 로더와 `ManifestProvider`
`%USERPROFILE%\.daiso\tools\*.yaml` 를 읽어 `IProvider` 를 만들어 DI 에 넣는다.
세션 읽기는 아직 없다 — **설치 여부 · 실행 · 규칙 파일까지만** 되는 도구다.
**완료 기준**: 매니페스트 하나를 놓고 앱을 켜면 새 터미널 카드에 도구가 넷이 되고, 실행이 된다.

### Stage 5 — 세션 로그 필드 매핑
§5 의 매핑을 구현한다. **검증은 Claude 재현**이다(§5).
**완료 기준**: Claude 를 매니페스트로만 기술해 Stage 0 의 기대값과 같은 `SessionInfo` 를 낸다.

### Stage 6 — 사람에게 보이게
설정 화면에 `도구 플러그인` 목록: 어디서 읽었는지 · 잘못된 매니페스트는 무엇이 틀렸는지 · 다시 읽기.
**완료 기준**: 일부러 깨뜨린 매니페스트를 놓아도 **앱이 뜨고**, 그 도구만 오류로 표시된다.

### Stage 7 (선택) — 어셈블리 플러그인
A 를 다 쓴 뒤에 필요가 남으면 검토한다. 그때 서명 · 버전 결합 · 격리를 따로 정한다.

---

## 7. 위험과 되돌리기

| 위험 | 대응 |
|---|---|
| **인덱스 재생성**(Stage 1) | 유일하게 되돌리기 어려운 단계라 Stage 0 특성화 테스트를 먼저 둔다. 재생성은 이미 있는 길이다(`IndexFormatVersion` 4 로 한 번 했다) |
| **모르는 도구 id 가 인덱스에 남는다** | 옛 행을 만나도 죽지 않고 "모르는 도구"로 흘려보낸다. 지금 `Enum.Parse` 는 던진다 — Stage 1 에서 같이 고친다 |
| **깨진 매니페스트가 앱을 못 뜨게 한다** | 로더는 매니페스트 하나가 실패해도 나머지를 살린다. 실패는 Stage 6 화면에 모아 보인다 |
| **매니페스트가 스크립트가 된다** | 치환은 `{USERPROFILE}` · `{PROJECT}` · `{id}` 셋뿐. 표현식·조건·반복을 넣지 않는다. 넣고 싶어지면 그건 B 로 갈 신호다 |
| **`models.command` 가 임의 명령을 돌린다** | 사람이 그 파일을 놓은 것이므로 앱 권한 밖은 아니지만, Stage 6 목록에 **어떤 명령을 돌리는지 그대로 보인다** |
| **표시 정보를 옮기다 화면이 미묘하게 바뀐다** | Stage 2 의 완료 기준을 "전과 같이 보인다"로 두고, `ToolLook` 을 창구로 남겨 부르는 곳을 안 건드린다 |

**되돌리기**: Stage 2~6 은 서로 독립이라 개별 되돌리기가 된다.
Stage 1 은 그 위 단계가 다 얹히므로, 완료 기준을 채우기 전에 다음으로 넘어가지 않는다.

---

## 8. 안 하기로 한 것

- **매니페스트에 스크립트·표현식** — §7 참고. 그 순간 데이터가 아니라 코드가 되고, 검사할 수 없어진다.
- **플러그인 장터 · 원격 설치** — 앱이 남의 파일을 받아 오지 않는다. 사람이 폴더에 놓는다.
- **`ToolKind` 를 남겨 둔 채 "기타" 값 하나로 때우기** — 인덱스·화면이 도구를 못 가른다. 결국 다시 뜯게 된다.

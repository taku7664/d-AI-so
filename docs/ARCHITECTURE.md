# d-AI-so 아키텍처 (v0.2)

> 요구사항: [REQUIREMENTS.md](REQUIREMENTS.md). 이 문서는 "어떻게 나누고 연결하는가"만 다룬다.
> v0.2: 2026-09-07 검수 반영 (인증 만료 기준, Codex 형식 변형, Claude 메시지 분류, Core 순수성, 증분 인덱싱 등).

---

## 1. 계층 구조

```
┌─────────────────────────────────────────────┐
│ Daiso.App        WinUI 3 · Views · ViewModels│  UI만. 로직 없음
├─────────────────────────────────────────────┤
│ Daiso.Core       모델 · 인터페이스 · 순수 로직 │  문자열/스트림만 다룸. 경로·OS·IO 없음
├──────────────────────┬──────────────────────┤
│ Daiso.Providers      │ Daiso.Infrastructure │
│  .Claude  .Codex     │  SQLite · FS · Process│  Core 인터페이스 구현
│  .Antigravity        │                      │
│  .Manifest (플러그인) │                      │
└──────────────────────┴──────────────────────┘
```

의존 방향: `App → Core ← Providers`, `App → Core ← Infrastructure`, `Infrastructure → Providers`(세션 인덱스가 Provider를 사용).
`Providers.Claude`/`Providers.Codex`/`Providers.Antigravity`/`Providers.Manifest` → `Providers.Common`.
Common은 Core만 참조한다 (§4.3 공용 코드가 Core 순수성 규칙을 지킬 수 없어 별도 어셈블리로 둔다).

`Providers.Manifest`는 **매니페스트 파일 한 장을 `IProvider`로 만든다** — 빌드된 앱에 도구를 더하는 길이다 (§9).
Core는 YamlDotNet 외에 아무것도 참조하지 않는다.

| 프로젝트 | TFM | 주요 패키지 |
|---|---|---|
| Daiso.Core | net8.0 | YamlDotNet |
| Daiso.Providers.Common | net8.0 | (없음) — 두 Provider가 공유하는 경로 정규화·jsonl 스트리밍·JWT exp 판독 |
| Daiso.Providers.Claude / .Codex | net8.0 | System.Text.Json |
| Daiso.Infrastructure | net8.0-windows | Microsoft.Data.Sqlite (SQLitePCLRaw.bundle_e_sqlite3, FTS5 포함), Microsoft.VisualBasic (휴지통) |
| Daiso.App | net8.0-windows10.0.19041 | Microsoft.WindowsAppSDK, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection |
| Daiso.Core.Tests / Daiso.Providers.Tests / Daiso.Infrastructure.Tests | net8.0 (Infra는 net8.0-windows) | xunit, FluentAssertions |

**Core 순수성 규칙**: Core의 어떤 타입도 `System.IO.File`, `Directory`, `Path.GetFullPath`, `Environment.GetFolderPath`, `Process`를 호출하지 않는다. 입력은 `string` / `TextReader` / `Stream`, 출력은 모델 또는 `string`.

---

## 2. Core 모델

### 2.1 Rule (PROJECT_RULES.daiso)

```csharp
public sealed record RulePreset(
    int Daiso,                      // 스키마 버전, 현재 1
    string Name,
    string? Description,
    IReadOnlyList<RuleAction> Global,
    IReadOnlyList<Rule> Rules);

public sealed record Rule(Condition When, IReadOnlyList<RuleAction> Then);
public sealed record RuleAction(string Action, Priority Priority = Priority.Should);
public enum Priority { Must, Should, May }

public abstract record Condition;
public sealed record LeafCondition(string Text) : Condition;
public sealed record AndCondition(IReadOnlyList<Condition> Items) : Condition;
public sealed record OrCondition(IReadOnlyList<Condition> Items) : Condition;
```

> **동등성 주의**: record의 기본 `Equals`는 `IReadOnlyList` 멤버를 참조 비교한다. 모델 비교는 반드시 `RulePresetSerializer.Serialize()` 결과 문자열 비교로 한다. (테스트 기준도 동일)

**YAML 매핑 (`RulePresetSerializer`)** — 입력 `string`/`TextReader`, 출력 `RulePreset` / `string`
- `when` 스칼라 → `LeafCondition`. 맵이면 키 정확히 하나: `and` | `or` | `not`
- `and`/`or` 값은 시퀀스(1개 이상), `not` 값은 단일 Condition
- `priority` 생략 → `Should`. 대소문자 무시
- 직렬화 키 순서 고정: `daiso, name, description, global, rules` / `when, then` / `action, priority`. `description` null이면 생략. `priority`는 Should여도 항상 기록
- **라운드트립 보장**: `Serialize(Parse(Serialize(Parse(x))))` == `Serialize(Parse(x))`. 주석·공백은 보존하지 않음

**검증 규칙 (`RuleValidator`)** — 파싱 후 실행, 위반 시 `RuleParseException(line, column, message)`
| 규칙 | 처리 |
|---|---|
| `daiso` 누락 또는 1 이외 | 오류 |
| `name` 빈 문자열 | 오류 |
| `when` 맵의 키가 0개 또는 2개 이상 | 오류 |
| 알 수 없는 키 (`when`/`then`/`action`/`priority`/최상위 5개 외) | 오류 |
| `and`/`or` 항목 0개 | 오류 |
| `and`/`or` 항목 1개 | 경고 없이 허용 (렌더 시 괄호 없이 단일로 출력) |
| `not` 안에 `not` | 허용 (렌더 `!!A`) |
| 리프 텍스트 공백만 | 오류 |
| `then` 항목 0개 | 오류 |
| `priority` 값이 MUST/SHOULD/MAY 외 | 오류 |

**마크다운 렌더 (`MarkdownRuleRenderer`)** — REQUIREMENTS 6.4 양식
- 조건 문자열화: `and` → `A & B`, `or` → `A | B`, `not` → `!A`
- 괄호: 부모와 연산자가 다른 복합 자식은 괄호로 감싼다. `not`의 자식이 복합이면 항상 괄호 `!(A & B)`. 리프는 괄호 없음
- 리프 텍스트에 `&`, `|`, `!`, `(`, `)`가 포함되면 리프를 큰따옴표로 감싼다 `"A & B 케이스" | C`
- 행동 줄: `{Action} ({MUST|SHOULD|MAY})`. 우선순위는 항상 표기

### 2.2 Session

```csharp
// enum 이 아니다. 바깥(플러그인)에서 값을 만들 수 있어야 한다 (§9)
public readonly record struct ToolKind   // .Id 는 소문자: "claude" | "codex" | "antigravity" | 플러그인 id
{
    public static ToolKind Claude { get; }        // "claude"
    public static ToolKind Codex { get; }         // "codex"
    public static ToolKind Antigravity { get; }   // "antigravity"
    public static IReadOnlyList<ToolKind> BuiltIn { get; }   // 플러그인이 못 쓰는 예약 id

    public string Id { get; }
    public static bool TryParse(string? id, out ToolKind kind);   // 대소문자 안 가림 (옛 기록의 "Claude")
}

public sealed record SessionInfo(
    ToolKind Tool,
    string Id,
    string FilePath,
    string? ProjectPath,            // 정규화된 경로 (§4.3). null이면 알 수 없음
    DateTimeOffset StartedAt,
    DateTimeOffset ModifiedAt,
    long SizeBytes,
    int UserMessageCount,           // 사용자가 직접 입력한 메시지 수만
    int AssistantMessageCount,
    string? FirstPrompt,            // 첫 사용자 메시지 앞 200자
    TokenUsage Usage,
    string? ToolVersion,            // Claude: version, Codex: cli_version
    bool IsArchived,                // Codex archived_sessions
    bool IsActive);                 // 현재 실행 중 (§4.1). 삭제 차단용

public sealed record TokenUsage(long Input, long Output, long CacheCreate, long CacheRead, string? Model)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0, null);
    public TokenUsage Add(TokenUsage o) => ...;   // Model은 첫 non-null 유지
}

public sealed record SessionMessage(DateTimeOffset At, MessageRole Role, string Text, bool IsSidechain);
public enum MessageRole { User, Assistant, Tool, System }
```

메시지 분류 원칙 (검색·카운트·내보내기 공통):
- `User`: 사람이 입력한 텍스트만. 도구 결과, meta, 시스템 주입 제외
- `Assistant`: 모델 텍스트 응답. 도구 호출 블록은 제외
- `Tool`: 도구 호출/결과. 검색 인덱스에 **넣지 않음**. 내보내기에서는 옵션
- `System`: 시스템/컴팩션 요약 등. 인덱스에 넣지 않음
- `IsSidechain=true`(서브에이전트)는 카운트·FirstPrompt에서 제외, 인덱스에는 넣음

### 2.3 Auth

```csharp
public enum AuthState { LoggedIn, ExpiringSoon, Expired, Missing }

public sealed record AuthStatus(
    ToolKind Tool,
    AuthState State,
    string? AccountLabel,           // 표시용. Claude: "{displayName} · {organizationName} · {subscriptionType}", Codex: "{auth_mode}"
    string? Email,
    DateTimeOffset? SessionExpiresAt,   // **재로그인이 필요해지는 시각** (§4). 단기 액세스 토큰 만료가 아님
    IReadOnlyList<string> Extras);      // MCP 커넥터 이름 등. 토큰 값 절대 포함 금지
```

상태 판정: `SessionExpiresAt` 기준. null이고 파일 있음 → LoggedIn. 지남 → Expired. 7일 이내 → ExpiringSoon. 파일 없음 → Missing.

### 2.4 Context Doctor

```csharp
public sealed record ContextFile(string Path, string Kind, string Content, bool Exists, int Order); // Order: 로드 순서
public sealed record DuplicateLine(string NormalizedText, IReadOnlyList<string> Files);
public sealed record ConflictHint(string FileA, string LineA, string FileB, string LineB, string Reason);

public sealed record ProjectFacts(       // REQUIREMENTS 5.3 부가 정보
    string ProjectPath,
    IReadOnlyList<string> Settings,
    int SkillCount, int AgentCount, int CommandCount,
    bool HasMcpJson,
    string? GitBranch, string? GitCommit);

public sealed record ContextReport(
    ToolKind Tool,                  // 도구별로 읽는 파일이 다르므로 리포트도 도구별
    IReadOnlyList<ContextFile> Files,
    int TotalChars,
    IReadOnlyList<DuplicateLine> Duplicates,
    IReadOnlyList<ConflictHint> Conflicts);
```

---

## 3. 인터페이스

### 3.1 Core (순수)

```csharp
// YAML ↔ 모델. 파일 아님
public interface IRulePresetSerializer
{
    RulePreset Parse(string yaml);            // 검증 포함
    string Serialize(RulePreset preset);
}

public interface IMarkdownRuleRenderer
{
    string Render(RulePreset preset);
}

// 마커 블록 삽입/갱신. 파일 내용 문자열을 받아 새 내용을 반환
public interface IInstructionMarkerWriter
{
    string Apply(string? existingContent, string instructionBody);
    // existingContent null → 새 파일 내용. 마커 있음 → 블록 내부만 교체. 없음 → 끝에 빈 줄 + 블록 추가
    // 마커 밖 내용은 바이트 단위 불변 (개행 문자 종류 포함)

    string Strip(string content);
    // 마커 블록(마커 두 줄 포함)과 블록이 남기는 빈 줄을 제거한 나머지. 마이그레이션 diff·복사의 입력
}

// 도구별 지시문 본문 생성
public interface IInstructionTemplate
{
    string For(ToolKind tool, string rulesFileName /* "PROJECT_RULES.daiso" */);
    // Claude: "@PROJECT_RULES.daiso" import 한 줄 + 우선순위 설명 한 줄
    // Codex : "Read and follow ./PROJECT_RULES.daiso (YAML) ..." 지시문
}

// Context Doctor 분석. 파일 읽기는 호출자 책임
public interface IContextAnalyzer
{
    ContextReport Analyze(ToolKind tool, IReadOnlyList<ContextFile> files);
}

// CLAUDE.md ↔ AGENTS.md 마이그레이션 (REQUIREMENTS §7). 파일 읽기·쓰기는 호출자 책임
public interface IInstructionMigrator
{
    InstructionMigrationPlan Plan(InstructionSource claude, InstructionSource codex);
    MigrationResult Render(InstructionMigrationPlan plan, MigrationDirection direction);
}

public enum MigrationDirection { ClaudeToCodex, CodexToClaude }
public enum DiffKind { Same, Added, Removed }

public sealed record DiffLine(DiffKind Kind, int? LeftLine, int? RightLine, string Text);

// Imports: 원본에 있는 `@경로` → 읽어온 내용 (null이면 못 읽음 → 경고)
public sealed record InstructionSource(bool Exists, string? Content, IReadOnlyDictionary<string, string?> Imports);

public sealed record InstructionMigrationPlan(
    InstructionSource Claude, InstructionSource Codex,
    string ClaudeBody, string CodexBody,          // 마커 블록을 뺀 본문
    IReadOnlyList<DiffLine> Diff,                 // 왼쪽=CLAUDE.md, 오른쪽=AGENTS.md
    MigrationDirection? Suggested,                // 한쪽만 있으면 그 방향, 둘 다 있으면 null(사용자 선택)
    IReadOnlyList<string> Notes);

public sealed record MigrationResult(ToolKind Target, string Content, IReadOnlyList<string> Warnings);
```

### 3.2 Provider (Providers 프로젝트 구현)

```csharp
public interface IProvider
{
    ToolKind Kind { get; }
    ToolDisplay Display { get; }                    // 이름·제작사·색·로고. 기본 구현 없음 (§9)
    string ExecutableName { get; }                  // "claude" | "codex" — npm 셸(.cmd)만 사용
    string RulesFileName { get; }                   // "CLAUDE.md" | "AGENTS.md"
    IReadOnlyList<string> ContextFilePatterns(string projectDir);   // §4.4 로드 순서대로
    Task<bool> IsInstalledAsync(CancellationToken ct);
    Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct);
    IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(CancellationToken ct);   // 메타만. 본문 파싱 없음
    Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct); // 본문 스캔해 카운트·usage 채움
    IAsyncEnumerable<SessionMessage> ReadMessagesAsync(string filePath, long fromByteOffset, CancellationToken ct);
    string BuildResumeArguments(SessionInfo session);   // "--resume <id>" | "resume <id>"

    IReadOnlyList<AuthFile> AuthFiles { get; }   // 로그인 상태를 이루는 파일 (§5.7 프로필)

    Task<IReadOnlyList<ModelOption>> ListModelsAsync(CancellationToken ct) => [];   // 새 터미널 카드의 모델 칸. 못 읽으면 빈 목록
}

public sealed record AuthFile(string Path, bool Required);
public sealed record ModelOption(string Id, string Name, string? Description = null);   // Id 가 --model 뒤에 그대로 붙는다
```

**모델 목록 (`ListModelsAsync`) — 얻는 길이 도구마다 다르다** (2026-09-10 이 PC 에서 확인)

| 도구 | 어디서 | 믿을 만한가 | 구현 |
|---|---|---|---|
| Antigravity (agy 1.1.28) | 공식 명령 `agy models` → 한 줄에 `id<TAB>이름`. 첫 줄 `Fetching available models...` 는 탭이 없어 건너뛴다 | 공식. 서버에 물어 몇 초 걸리고 인터넷이 필요하다 → 30초에 끊는다. 추론 강도가 id 에 붙어 있다(`-high`) | `AntigravityModelList`, `ICommandRunner` |
| Codex (0.153.4) | CLI 가 받아 둔 `~/.codex/models_cache.json` 의 `models[]` (`slug` · `display_name` · `description` · `visibility`). `visibility: "hide"` 는 뺀다 | **문서에 없는 내부 파일.** CLI 를 한 번도 안 돌렸으면 없다 | `CodexModelCache` |
| Claude (2.1.266) | **목록 명령이 없다.** `claude --help` 가 드는 별칭 `fable` · `opus` · `sonnet` + `~/.claude.json` 의 `additionalModelOptionsCache[]` (`value` · `label` · `description`, 계정에 따라 붙는 모델) | 별칭은 공식. 뒤의 배열은 **문서에 없는 내부 항목** | `ClaudeModelList` |

- 어느 쪽이든 못 읽으면 빈 목록이지 예외가 아니다. 사람은 인자 칸에 `--model` 을 직접 적을 수 있다
- 모델 칸은 인자 칸의 `--model` 을 **고쳐 쓴다** (`ModelArgument.Apply`/`Read`). 따로 들고 있다가 실행 때 붙이면 사람이 적은 `--model` 과 겹쳐 어느 쪽이 이기는지 알 수 없다. 칸이 곧 실행될 인자다
- 이미 열린 방에서 바꾸는 것은 앱이 하지 않는다. CLI 안의 `/model` 이 그 일을 한다

### 3.3 Infrastructure

```csharp
public interface IRuleFileService          // 경로 기반. Core의 Serializer/Renderer/MarkerWriter를 조합
{
    RulePreset Load(string path);
    void Save(RulePreset preset, string path);
    void EnsureInstruction(string projectDir, IEnumerable<IProvider> providers);
}

public interface IInstructionMigrationService   // §5.6. Core의 IInstructionMigrator에 파일 IO를 붙인다
{
    InstructionMigrationPlan Plan(string projectDir);
    MigrationResult Apply(string projectDir, MigrationDirection direction, bool dryRun);
}

public interface ISessionIndex
{
    Task RebuildAsync(IProgress<IndexProgress> progress, CancellationToken ct);
    Task RefreshAsync(CancellationToken ct);      // §5.1 증분
    Task<IReadOnlyList<SessionInfo>> ListAsync(SessionFilter filter, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
public sealed record IndexProgress(int Done, int Total, string CurrentFile);
public sealed record SessionFilter(ToolKind? Tool, string? ProjectPath, DateOnly? From, DateOnly? To, bool OrphansOnly, long? MinSizeBytes, bool IncludeArchived = true);
public sealed record SearchHit(SessionInfo Session, SessionMessage Message, string Snippet);
public sealed record UsageSummary(IReadOnlyList<UsageDay> Days, IReadOnlyDictionary<string, TokenUsage> ByProject, IReadOnlyDictionary<string, TokenUsage> ByModel);
public sealed record UsageDay(DateOnly Date, TokenUsage Usage);

public interface IFileDisposer
{
    Task<DisposeResult> MoveToRecycleBinAsync(IEnumerable<SessionInfo> sessions);   // IsActive=true 항목은 거부하고 결과에 사유 기록
    Task<DisposeResult> DeletePermanentlyAsync(IEnumerable<SessionInfo> sessions);
}
public sealed record DisposeResult(IReadOnlyList<string> Deleted, IReadOnlyList<(string Path, string Reason)> Skipped);

public interface ITerminalLauncher
{
    Task LaunchAsync(string workingDir, string command, string arguments);
}

public interface IContextInspector       // 파일 읽기 + IContextAnalyzer 호출
{
    Task<ContextReport> InspectAsync(ToolKind tool, string projectDir, CancellationToken ct);
}

// REQUIREMENTS 5.3의 부가 정보(settings 요약, skills/agents/commands 수, .mcp.json, git 브랜치·커밋).
// git 명령을 실행하지 않고 .git 안의 파일만 읽는다.
public interface IProjectFactsReader
{
    Task<ProjectFacts> ReadAsync(string projectDir, CancellationToken ct);
}

public interface ISessionExporter
{
    Task ExportMarkdownAsync(SessionInfo session, string outputPath, ExportOptions options, CancellationToken ct);
}
public sealed record ExportOptions(bool IncludeToolCalls = true, bool IncludeSystem = false, bool IncludeSidechain = false);
```

---

## 4. Provider 구현 메모

**로그인 카드의 줄은 뜻이 고정이다**: 계정 이름 → 이메일 → 요금제 → 만료 → 설치.
`AuthStatus` 의 `AccountLabel`·`Email`·`Plan`·`SessionExpiresAt` 이 그 자리에 하나씩 대응하고, 값이 없으면
화면이 줄을 지우는 대신 `… 정보 없음` 을 넣는다. 도구마다 줄 수나 줄 뜻이 달라지면 카드 셋을 나란히 놓고
같은 자리를 비교할 수 없다 (2026-09-11 사람의 지적). 그래서 **구독·요금제를 이름 줄에 섞지 않는다** —
`AccountShapeTests` 가 그것을 잠근다. 같은 값을 부가 정보에서 한 번 더 말하지도 않는다.

모든 파일 읽기는 **UTF-8 명시** (`new StreamReader(path, Encoding.UTF8)`). 시스템 로케일(CP949)에 의존하지 않는다.

### 4.1 Claude (`Daiso.Providers.Claude`)

**인증**
- `%USERPROFILE%\.claude\.credentials.json` → `claudeAiOauth`
  - `refreshTokenExpiresAt` (ms epoch) → **`SessionExpiresAt`**. `expiresAt`은 자동 갱신되는 단기 액세스 토큰이라 상태 판정에 쓰지 않는다 (확인: 액세스 토큰 만료 후에도 CLI 정상 동작)
  - `subscriptionType`, `rateLimitTier`, `scopes` → Extras (`구독:`·`사용량 등급:`·`권한:` — 필드 이름을 그대로 옮기지 않는다)
  - `mcpOAuth` 키 이름(`|` 앞부분)만 → Extras
- `%USERPROFILE%\.claude.json` → `oauthAccount.{emailAddress, displayName, organizationName}` → `Email`, `AccountLabel`. 파일이나 키가 없으면 null
- `subscriptionType` → **`Plan`**. `AccountLabel` 에 섞지 않는다 (4장 머리의 카드 줄 규칙)

**세션**
- 루트: `%USERPROFILE%\.claude\projects\*\*.jsonl` (하위 폴더 `memory/` 등은 무시)
- 실행 중 판정: `%USERPROFILE%\.claude\sessions\<pid>.json` 을 읽어 세션 ID 집합을 만들고, 해당 pid가 살아 있으면 `IsActive=true`
- 레코드 필드
  - 공통: `type`, `sessionId`, `timestamp`, `cwd`, `version`, `isSidechain`, `isMeta`
  - `type: "user"` → `message.content`
    - 문자열 → **User**
    - 배열이고 `text` 블록만 → **User** (text 이어붙임)
    - 배열에 `tool_result` 포함 → **Tool** (`toolUseResult` 필드가 함께 있음)
    - `isMeta: true` → 제외
  - `type: "assistant"` → `message.content` 배열의 `text` 블록 → **Assistant**. `tool_use` 블록은 Tool로 분류. `message.usage.{input_tokens, output_tokens, cache_creation_input_tokens, cache_read_input_tokens}`, `message.model` 합산. `model == "<synthetic>"` 은 usage 합산과 ByModel에서 제외
  - `type: "system"` → System
  - 그 외 (`attachment`, `queue-operation`, `last-prompt`, `custom-title`, `mode`, `bridge-session`, `atis-latch` 등) → 무시
- `ProjectPath`: 첫 `cwd`. `StartedAt`: 첫 `timestamp`. `ToolVersion`: 첫 `version`
- 파일은 수십 MB 가능. 스트리밍 필수, 파싱 실패 줄은 건너뛰고 카운트만
- resume: `claude --resume <sessionId>`

### 4.2 Codex (`Daiso.Providers.Codex`)

**인증**
- `%USERPROFILE%\.codex\auth.json` → `auth_mode`, `last_refresh`, `OPENAI_API_KEY`(null 여부만), `tokens.{id_token, access_token, refresh_token, account_id}`
- **`SessionExpiresAt`**: `access_token`의 JWT payload `exp` (서명 검증 없이 base64 디코드만, 값은 exp 숫자만 사용). 디코드 실패 시 null → LoggedIn
- API 키 모드(`auth_mode == "apikey"`)는 만료 없음 → null
- 계정: `id_token` payload 의 `email` → `Email`, 요금제(`https://api.openai.com/auth`.`chatgpt_plan_type`, 없으면 최상위 `chatgpt_plan_type`) → `Plan`.
  사람 이름을 읽을 곳이 없어 `AccountLabel` 은 null 이다.
  `JwtReader` 가 payload 에서 **만료·이메일·요금제 세 조각만** 꺼내고 원문은 버린다 (§7.1)
- **인증 방식을 계정 이름 자리에 넣지 않는다.** 전에는 `auth_mode`(`chatgpt`)를 `AccountLabel` 로 넘겨 계정처럼 보였고,
  같은 값이 부가 정보에 한 번 더 나왔다 (2026-09-11 사람의 지적). 읽을 것이 없으면 비운다 — §4.5 의 이메일 규칙과 같다
- 부가 정보는 **JSON 필드 이름을 옮기지 않는다.** `인증 방식: ChatGPT 로그인`·`요금제: Plus`·`마지막 갱신: …`·`API 키: 설정됨` 처럼
  사람이 읽는 문구 키로 바꾼다(값→키 매핑은 Provider, 문구는 resw). 모르는 `auth_mode` 는 값을 그대로 보여 준다.
  `tokens.account_id` 는 사람이 쓸 데가 없어 보여 주지 않는다

**세션**
- 루트: `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl` + `%USERPROFILE%\.codex\archived_sessions\**\*.jsonl` (`IsArchived=true`)
- 첫 줄 `type: "session_meta"` → `payload.{id, cwd, timestamp, cli_version}`
- **버전별 형식 차이 (필수 대응)**. `cli_version`으로 분기하지 말고 두 형식을 모두 인식한다
  | 항목 | 구형 (≤0.147) | 신형 (0.153~) |
  |---|---|---|
  | 사용자 메시지 | `event_msg.payload.type == "user_message"` → `payload.message` | `event_msg.payload.type == "item_completed"` 이고 `payload.item.type == "UserMessage"` → `item.content[]` text |
  | 어시스턴트 | `event_msg.payload.type == "agent_message"` | `response_item.payload.type == "message"` 이고 `role == "assistant"` → `content[].text` (양쪽 공통으로도 존재. 중복 방지: `event_msg.agent_message`가 있으면 그것만, 없으면 response_item 사용) |
- 무시 대상: `world_state` (AGENTS.md 전문 포함, 인덱스 오염), `turn_context`, `token_usage_record`, `reasoning`, `function_call*`, `custom_tool_call*`, `inter_agent_communication_metadata`, `compacted`(요약은 System으로만)
- `response_item.message`에서 `role == "developer"` 또는 `"system"` → System
- 토큰: `event_msg.payload.type == "token_count"` 의 `payload.info.total_token_usage` — **누적값**. 마지막 non-null만 사용. `info`가 null인 레코드 있음
  - 매핑: `input_tokens`→Input, `output_tokens`→Output, `cache_write_input_tokens`(없으면 0)→CacheCreate, `cached_input_tokens`→CacheRead
  - 모델: `event_msg.thread_settings_applied.thread_settings.model` 또는 `turn_context.payload.model`
- resume: `codex resume <id>`

### 4.5 Antigravity (`Daiso.Providers.Antigravity`)

> **도구가 바뀌었다 (2026-06-18).** Google 이 2026-05-19 I/O 에서 Gemini CLI 와 Gemini Code Assist IDE 확장을 Antigravity 로 합친다고 알리고,
> 2026-06-18 부터 개인 계정(무료·AI Pro·Ultra)에서 Gemini CLI 가 요청을 멈췄다(기업 라이선스는 예외).
> 대체는 **Antigravity CLI** — 명령 `agy`, Go 로 만든 비공개 단일 실행 파일, npm 이 아닌 공식 설치 스크립트.
> 그래서 이 제공자는 **두 시대를 함께 다룬다**: 실행·설치·설정은 Antigravity 것이고, 디스크에 남은 기록·로그인 파일은 은퇴한 Gemini CLI 것이다.
> 옛 기록을 버릴 이유가 없으므로 계속 읽어 목록·검색·사용량에 보여 준다. 그래서 형식 파서의 이름은 `GeminiTranscriptReader`·`GeminiProjectMap` 으로 남겼다 —
> 파싱 대상이 그 형식이기 때문이다.
>
> **`agy` 1.1.28 을 이 PC 에 깔고 `agy --help` 로 확인한 것 (2026-09-09)**
> | 무엇 | 값 | 비고 |
> |---|---|---|
> | 시작 프롬프트 | `-i "메시지"` (= `--prompt-interactive`) | **`-p` 가 아니다.** `-p`(= `--print`)는 한 번 답하고 끝나는 비대화 모드라 방을 열어 두는 이 화면과 맞지 않는다. 추측으로 `-p` 를 넣었다가 여기서 바로잡았다 |
> | 이어서 열기 | `--conversation <id>` | **`--resume` 는 없다** — 은퇴한 Gemini CLI 의 플래그였다. 최근 대화만 이어려면 `--continue`(`-c`) |
> | 인자 프리셋 | `--model ` · `--sandbox` | `--sandbox` 는 값 없는 스위치. 그 밖에 `--mode`(accept-edits\|plan) · `--effort`(low\|medium\|high) · `--add-dir` · `--dangerously-skip-permissions` 가 있다 |
> | 버전 | `agy --version` → `1.1.28` | |
> | 설치 위치 | `%LOCALAPPDATA%\agy\bin\agy.exe` (약 181MB) | 설치 스크립트가 **사용자 PATH 레지스트리**에 등록하고 브로드캐스트한다. 이미 떠 있는 프로세스는 그 PATH 를 못 보므로 `IsInstalledAsync` 는 이 경로도 직접 본다 |
> | 설치 스크립트가 하는 일 | 매니페스트 JSON → 바이너리 내려받기 → **SHA512 대조** → 복사 → `agy.exe install` 로 PATH 설정 | 관리자 권한 없이 사용자 폴더만. 내려받는 곳은 Google Cloud Run 인스턴스 |
>
> **로그인하고 대화까지 해 본 뒤 확인한 것 (2026-09-09 실측)**
> | 무엇 | 값 |
> |---|---|
> | 설정 | `~/.gemini/antigravity-cli/settings.json` — `colorScheme` · `model` · `trustedWorkspaces`. **첫 실행 때 생긴다**(설치 직후에는 없다) |
> | 로그인 토큰 | **파일에 없다.** Windows 자격 증명 관리자의 일반 자격 증명 `gemini:antigravity` (`cmdkey /list` 에 `LegacyGeneric:target=gemini:antigravity`) |
> | 계정 이메일 | 읽을 수 있는 파일에 없다 (로그 파일 안에만 있다) |
> | 대화 기록 | **암호화가 아니다.** `~/.gemini/antigravity-cli/brain/{대화 id}/.system_generated/logs/transcript.jsonl` — 한 줄에 한 단계인 평범한 JSONL 이다: `step_index` · `source`(USER_EXPLICIT\|MODEL) · `type`(USER_INPUT\|PLANNER_RESPONSE…) · `status` · `created_at` · `content` · `tool_calls[]`. `transcript_full.jsonl` 도 있다 |
> | 그 밖 | `conversations/{id}.db`(SQLite) · `cache/conversation_metadata.json`(제목·미리보기·작업 폴더) · `history.jsonl`(입력 이력) · `annotations/*.pbtxt` |
>
> 앞서 이 문서는 "IDE 가 `.pb` 로 암호화하니 CLI 도 그럴 것"이라고 적었다. **틀렸다.** IDE 의 `~/.gemini/antigravity/conversations/*.pb` 는 실제로 암호화돼 있지만
> CLI 는 별도 폴더에 평문 JSONL 로 쓴다. 한 제품군이라고 저장 방식이 같을 것이라 미루면 안 된다.
>
> **아직 확인하지 못한 것**
> | 무엇 | 지금 코드 | 왜 모르는가 |
> |---|---|---|
> | 세션 목록·검색·사용량 연결 | 옛 `~/.gemini/tmp/**/chats/*.jsonl` 만 읽는다 | 형식은 위와 같이 확인됐으나 **아직 구현하지 않았다**. 토큰 수를 어디서 얻는지, 대화 id 와 프로젝트 폴더를 무엇으로 잇는지(추정: `cache/conversation_metadata.json` 의 `WorkspaceURIs`)를 더 봐야 한다 |
> | 컨텍스트 파일 이름 | `GEMINI.md` (옛 값) | `agy --help` 에 관련 플래그가 없다. `builtin/skills/agy-customizations/docs/rules.md` 를 읽어 확인할 수 있다 |
> | 사용자 슬래시 명령 위치 | 옛 `~/.gemini/commands/*.toml` | `agy` 는 확장 대신 플러그인(`agy plugin`)을 쓴다 |

> 근거(옛 Gemini CLI 기록 형식): 이 PC의 `~/.gemini` 실제 파일(`oauth_creds.json`·`google_accounts.json`·`projects.json`·`tmp/{이름|해시}/chats/session-*.jsonl` 헤더 줄)과,
> 설치한 Gemini CLI 0.58.0 번들의 `chatRecordingTypes.ts`/`chatRecordingService.ts`/`sessionOperations.ts` 소스를 대조했다: 첫 줄은 `sessionId`가 있는 헤더 레코드, 메시지는 `type: user|gemini|…`, `content`(문자열 또는 Part 배열), `thoughts`, `toolCalls`, `tokens {input, output, cached, thoughts, tool}` (usageMetadata에서 그대로 옮긴 값). 파일 이름은 `session-…-<짧은 id>.json|.jsonl`.
> 아직 안 다루는 것: 옛 단일 `.json` 파일(통째로 다시 쓰이는 형식이라 증분 인덱스와 맞지 않음), `content` 안에 `functionCall` 조각으로 들어간 도구 호출.

**인증**
- `%USERPROFILE%\.gemini\oauth_creds.json` → `access_token`, `refresh_token`, `expiry_date`(ms), `token_type`, `scope`. **값은 읽지 않는다**
- `refresh_token`이 있으면 CLI가 알아서 갱신하므로 만료 개념 없음 → `SessionExpiresAt = null` → LoggedIn. 없으면 `expiry_date`로 판정
- 계정: `google_accounts.json`의 `active` 이메일. `AccountLabel`과 `Email` 둘 다 이 값
- **판정의 근거는 자격 증명 관리자 항목 `gemini:antigravity` 하나뿐이다.** 있으면 LoggedIn, 없으면 Missing.
  `ICredentialProbe`(`WindowsCredentialProbe` → `CredReadW`)가 **존재만** 본다. 이 API 에 "있는지만 묻기"가 없어 구조체가 잠깐 메모리에 오지만
  `CredentialBlob` 은 건드리지 않고 곧바로 `CredFree` 한다. 토큰은 화면·로그·파일 어디에도 쓰지 않는다
- **만료는 알 수 없다**(`SessionExpiresAt = null`). CLI 가 알아서 갱신하고 앱은 그 시각을 볼 수 없다. 부가 정보에 `만료: 알 수 없음`으로 이유를 적는다 —
  아무 말이 없으면 앱이 못 읽는 것인지 로그인이 안 된 것인지 구분할 수 없다
- 요금제도 알 수 없다 → `Plan = null` → 카드는 `요금제 정보 없음`
- **이메일 자리는 비운다.** 읽을 수 있는 계정 파일은 은퇴한 Gemini CLI 의 `google_accounts.json` 뿐이고,
  다른 계정으로 `agy` 에 로그인했으면 그 값은 틀린 값이다. 부가 정보에 `계정 이메일: CLI가 파일에 남기지 않습니다`로 밝힌다
- **은퇴한 Gemini CLI 의 `oauth_creds.json` 은 이 도구의 상태가 아니다.** 처음 이식할 때는 그 파일의 `refresh_token`·`expiry_date`·`scope` 를
  Antigravity 카드에 그대로 얹었는데, 죽은 도구의 액세스 토큰 만료를 이 도구 것처럼 보여 주는 셈이었다.
  이제는 파일이 있으면 `참고: … (이 도구와 무관)` 한 줄만 적는다. `AntigravityAuthTests` 가 그 파일에서 토큰 사실이 새어 나오지 않는지 잠근다
- `settings.json` 의 `apiKey` 가 있으면 `인증 방식: API 키`, 없으면 `브라우저 로그인`. `model` 값이 있으면 `고른 모델`로 보여 준다
- 프로필(§5.7) 파일: `antigravity-cli/settings.json`(선택). **필수 파일이 없다** — 자격 증명이 파일이 아니라 프로필로 로그인을 옮길 수 없다

**세션**
- 루트: `%USERPROFILE%\.gemini\tmp\{프로젝트 이름 | SHA-256}\chats\session-*.jsonl`. 아카이브 개념 없음
- **append-only 로그가 아니다. 레코드 네 가지를 순서대로 리플레이해야 최종 상태가 나온다** (`GeminiTranscriptReader`, CLI 0.58 읽기 코드와 같은 규칙)
  | 레코드 | 판별 | 뜻 |
  |---|---|---|
  | 헤더 | `sessionId`·`projectHash` 문자열 | 첫 줄. `startTime`. `sessionId == "a2a-server"`(Antigravity 서버 세션)는 목록에서 뺀다 |
  | 메시지 | `id` 문자열 | 같은 id가 이미 있으면 그 자리에서 덧씀(응답 토큰이 나중에 붙는다). 없으면 뒤에 붙임 |
  | `$set` | `$set` 객체 | `$set.messages` 배열이 있으면 **목록 전체 교체**(컨텍스트 관리가 쓴다). `lastUpdated`·`memoryScratchpad`·`summary`는 무시 |
  | `$rewindTo` | 문자열 | 그 id부터 끝까지 잘라냄 |
- 그래서 `IProvider.AppendOnlySessions = false`. 인덱스는 이 도구의 파일이 바뀌면 오프셋을 버리고 처음부터 다시 읽는다(§5.1). `ReadMessagesAsync`의 오프셋도 무시한다
- 메시지 → `type == "user"` → User, `"gemini"` → Assistant(`content`는 문자열 또는 `[{text}]` 조각), `"info"|"warning"|"error"` → System, `toolCalls[]` → 각각 Tool 한 건(이름 + 인자 앞 300자 + 상태. 결과 본문은 넣지 않는다)
- 프로젝트 경로: `projects.json`의 `{"projects": {"소문자 경로": "이름"}}`으로 폴더 이름 → 경로, 또는 SHA-256(경로) → 경로를 되짚는다. 모르면 null. 키가 소문자 경로라 `ProjectPathNormalizer.RestoreCasing`으로 디스크의 실제 대소문자를 되돌려 저장한다. 그래야 Claude·Codex의 같은 프로젝트와 한 묶음이 된다(§4.3)
- 토큰: 메시지의 `tokens {input, output, cached, thoughts, tool}` — **응답마다 붙는 값이라 날짜별로 더한다**. Input = input + tool, Output = output + thoughts, CacheRead = cached, CacheCreate 0. 모델은 `model`
- resume: `agy --conversation <id>` (확인). 최근 대화만 이어려면 `--continue`
- 실행 파일: `agy` (`%LOCALAPPDATA%\agy\bin\agy.exe`). `ExecutableLocator` 가 `.exe` 도 찾는다.
  다만 설치 감지(`IsInstalledAsync`)는 **PATH 와 기본 설치 경로를 함께** 본다 — 설치 스크립트는 사용자 PATH 레지스트리에 등록하므로 이미 떠 있던 앱의 PATH 사본에는 없다
- **띄울 때도 같은 문제가 있다.** 이름만 셸에 넘기면 그 셸도 앱의 낡은 환경을 물려받아 "그런 명령 없다"고 한다.
  그래서 `IProvider.LaunchTarget`(기본값은 `ExecutableName`)을 두고, Antigravity 는 PATH 에서 못 찾으면 **절대 경로**를 준다.
  앱을 다시 켜지 않아도 방금 깐 도구가 열린다. 띄우는 곳·미리보기는 전부 이 값을 쓴다 — 이름은 문서·테스트가 잠그는 고정값으로 남긴다.
  경로에 빈칸이 있을 수 있으므로 `TerminalCommandBuilder` 가 명령을 따옴표로 감싼다
- 설치: **앱은 명령을 돌리지 않고 공식 안내 페이지(`https://antigravity.google/docs/cli/install/`)를 브라우저로 연다** (`IProvider.InstallUri`, `IUriOpener`).
  공식 명령은 `irm https://antigravity.google/cli/install.ps1 | iex` 이고 화면에는 그대로 보여 주지만 실행은 사람이 한다.
  이유: 원격 스크립트를 메모리에서 실행하는 이 꼴은 **백신이 흔히 차단한다**(이 PC 에서 실제로 차단됐다). 앱이 사용자에게 백신을 끄라고 할 수는 없고,
  앱이 남의 스크립트를 대신 실행해 주는 것도 옳지 않다. npm 도구(Claude·Codex)는 `InstallUri` 가 null 이라 지금처럼 새 터미널에서 명령을 돌린다.
  `InstallCommandTests` 가 "npm 이 아님"과 "안내 페이지는 공식 도메인"을 잠근다
- 안 깔린 도구는 시작 버튼이 곧 설치 버튼이므로 **3단계(무엇부터 할까요?)를 묻지 않는다**(`CanStart`) — 설치에 세션 선택이 필요 없는데 잠가 두면 깔러 온 사람이 막힌다.
  대안으로 확인한 것: winget 미제공(이 PC 에는 winget 자체가 없다), GitHub 릴리스에 `agy_cli_windows_x64.zip` 이 있으나
  그 조직(`google-antigravity`)이 GitHub 인증 조직이 아니고 라이선스도 없어 앱이 사용자를 그쪽으로 보내지 않는다.
  실제로 깔아 보니 백신이 아니라 **PowerShell 실행 정책**(서명 없는 스크립트 거부)이 먼저 막았다. 스크립트를 파일로 받아
  `pwsh -ExecutionPolicy Bypass -File` 로 그 프로세스에만 우회해 통과했다 — 영구 정책은 건드리지 않는다
- 설정: `~/.gemini/antigravity-cli/settings.json`·`keybindings.json`
- 내장 슬래시 명령: `/agents` `/boost` `/clear` `/config` `/fork` `/keybindings` `/permissions` `/resume` `/rewind` (공식 CLI 참고 문서)

**지시문**
- 파일 `GEMINI.md` — **미확인**(위 표). `PROJECT_RULES.daiso`는 Codex처럼 읽기 지시문으로 넣는다(`InstructionTemplate`). 읽기 지시문은 어느 쪽이든 통한다
- 마이그레이션(§5.6)은 CLAUDE.md ↔ AGENTS.md 두 방향만이다. GEMINI.md는 연동(마커 블록)만 받는다

### 4.3 경로 정규화 (`ProjectPathNormalizer`, `Daiso.Providers.Common`)
- `Path.GetFullPath` 후 드라이브 문자 대문자, 끝 구분자 제거, `\` 통일
- 그룹핑·필터·OrphansOnly 판정은 정규화 값으로만

### 4.4 Context 파일 목록 (`ContextFilePatterns`, 로드 순서대로)

| Claude | Codex | Antigravity |
|---|---|---|
| `~/.claude/CLAUDE.md` | `~/.codex/AGENTS.md` | `~/.gemini/GEMINI.md` |
| 루트→상위 방향: `{ancestor}/CLAUDE.md`, `{ancestor}/.claude/CLAUDE.md` (드라이브 루트까지) | git 루트→cwd 방향: `{dir}/AGENTS.md` | git 루트→cwd 방향: `{dir}/GEMINI.md` |
| `{dir}/CLAUDE.md`, `{dir}/.claude/CLAUDE.md`, `{dir}/CLAUDE.local.md` | `{dir}/AGENTS.md` | `{dir}/GEMINI.md` |
| `{dir}/.claude/rules/*.md` | — | — |
| `{dir}/PROJECT_RULES.daiso` | `{dir}/PROJECT_RULES.daiso` | `{dir}/PROJECT_RULES.daiso` |

---

## 5. 데이터 흐름

### 5.1 세션 인덱싱 (증분)
```
RefreshAsync
  → 각 IProvider.EnumerateSessionsAsync  (파일 경로, size, mtime만)
  → sessions 테이블의 (size, mtime, last_offset) 와 비교
     · 신규          → offset 0부터 ReadMessagesAsync
     · size 증가     → last_offset부터 이어 읽기 (IProvider.AppendOnlySessions 인 도구만. Antigravity는 처음부터)
     · 파서 형식 버전(PRAGMA user_version) 이 코드의 상수와 다르면 → 표를 **버리고** 다시 만든 뒤 VACUUM, 전부 처음부터. 파서를 고치면 상수를 올린다
     · size 감소/변경 → offset 0부터 재파싱 (재작성된 경우)
     · 동일          → 건너뜀
  → messages 삽입(트리거가 FTS 따라감), usage_daily 갱신, last_offset = **다 읽은 뒤**의 파일 크기
     · 이 셋은 **한 트랜잭션**이다. 본문만 커밋하고 나오면, 그 사이에 죽었을 때 오프셋이 옛 값으로 남아 같은 자리를 다시 담는다
```
- SQLite: `%LOCALAPPDATA%\d-AI-so\index.db`
- **읽기와 쓰기는 연결을 나눈다.** 목록·검색·사용량은 호출마다 새 연결을 열고, 갱신·재구축은 전용 연결 + 세마포어로 직렬화한다.
  하나의 연결을 화면과 배경 갱신이 같이 쓰면 리더가 겹쳐 `IndexOutOfRange`로 깨진다 (WAL이라 읽기는 쓰기를 기다리지 않는다)
- **본문은 `messages`, 색인은 `messages_fts`(external content).** FTS5, **`tokenize='trigram'`**. 3글자 미만 검색어는 `LIKE` 폴백
  - `messages(id, file_path, at, role, hash, text)` + `ix_messages_file` + `ux_messages_key(file_path, at, role, hash)`.
    지우기는 `DELETE FROM messages` 하나로 하고 트리거가 FTS 를 따라 지운다 —
    예전에는 FTS 가 본문까지 들고 있었고 `file_path` 가 `UNINDEXED` 라 파일 하나 바뀔 때마다 표 전체를 훑었다
  - **같은 줄은 두 번 담지 않는다**(`INSERT OR IGNORE` + `ux_messages_key`). 중복은 세 곳에서 온다:
    도구가 같은 말을 두 레코드에 남기고(Codex), 이어 읽기 경계가 겹치고, 담는 도중에 죽으면 다음 갱신이 같은 자리를 다시 담는다.
    막는 자리를 저장소 한 곳으로 모았다 (2026-09-11 실측: 여분 행 1,953 개)
  - `detail=none` 은 **쓸 수 없다.** 색인이 절반으로 줄지만 trigram 은 세 글자 넘는 말을 삼각자 *구절*로 찾고,
    그 판에서는 구절 질의가 막혀 있다(`fts5: phrase queries are not supported`). 재어 보고 되돌렸다
  - **판이 바뀌면 VACUUM 한다.** `DELETE` 는 빈 쪽을 파일에 남긴다 — 판이 다섯 번 오르는 동안
    실측 557MB 까지 부풀었고 같은 내용을 새로 담으면 133MB 였다. 닫을 때 `wal_checkpoint(TRUNCATE)` 도 한 번 돈다(WAL 이 109MB 였다)
  - 검색은 **상한 200건**. 상한이 없던 때는 흔한 낱말 하나에 수만 줄의 본문 전체가 메모리로 올라왔다
- **목록·검색·사용량은 배경 스레드에서 돈다.** SQLite 읽기는 동기라, `Task.FromResult` 로 감싸면 전부 화면 스레드에서 돌아 창이 멈춘다
- `last_offset` 은 **다 읽은 뒤**의 파일 크기다. 열거 때 본 크기를 적던 때는, 제공자가 그 뒤로도 끝까지 읽으므로
  담는 동안 자란 줄이 다음 갱신에 또 담겼다 — 본문은 `ux_messages_key` 가 막지만 사용량은 더하기라 조금씩 부풀었다.
  남는 위험은 "끝에 닿은 순간과 크기를 재는 순간 사이"뿐이고, 그 사이에 붙은 줄은 파일이 또 자라므로 다음 갱신의 크기 비교가 잡는다
- `usage_daily(date, tool, project, model, input, output, cache_create, cache_read)`. Codex는 누적값이라 세션 단위로 **덮어쓰기**(세션 StartedAt 날짜에 귀속), Claude는 메시지 timestamp 날짜별 **합산**

### 5.2 .daiso 편집
```
열기 → IRuleFileService.Load(path)  = File.ReadAllText(UTF8) → IRulePresetSerializer.Parse
편집(트리) → RulePreset 재구성 → IMarkdownRuleRenderer.Render → 미리보기
저장 → Serialize → File.WriteAllText(UTF8, BOM 없음)
   → 옵션: EnsureInstruction(projectDir, providers)
        Claude: CLAUDE.md 마커 블록에 IInstructionTemplate.For(Claude) = "@PROJECT_RULES.daiso" import
        Codex : AGENTS.md 마커 블록에 For(Codex) = 읽기 지시문
```

**기본 제공 프리셋과 갤러리**

- `Daiso.Core/Resources/Presets/*.daiso` 14개가 임베디드 리소스로 들어 있다. 형식은 사용자 파일과 완전히 같고, `BuiltInPresets.List/Read`로 읽는다
- 카탈로그(순서·갈래)는 `BuiltInPresets.Catalog` 코드에 있다. 갈래는 `PresetCategory` — Language(C#, C++, Python, TypeScript, Rust, Unity), Workflow(Git, 테스트, 리뷰, 리팩터링, 보안, 문서, API), Communication(한국어 소통)
- 내 규칙 화면은 **왼쪽 목록 · 끌 수 있는 구분선 · 오른쪽 편집기** 골격이다(내 프롬프트와 같다). 왼쪽 목록은 기본 제공 + 내 라이브러리(`%LOCALAPPDATA%\d-AI-so\presets`). **고르면 그것이 편집기에 실린다**(내 프롬프트와 같다). 기본 제공은 경로 없이 열려 저장할 때 내 파일이 되므로 원본은 바뀌지 않는다. 목록 아래 `지금 규칙에 추가`는 편집 중인 것 뒤에 붙일 때만 쓴다. 목록을 다시 채울 때의 선택 복원은 편집기를 건드리지 않는다
  - `새로 열기` — 편집기에 그대로 연다. 기본 제공은 **경로 없이** 열려 저장할 때 내 파일이 된다 (원본은 바뀌지 않는다)
  - `지금 규칙에 추가` — 편집 중인 규칙 뒤에 붙인다. 같은 문장의 전역 행동은 한 번만, 새 문서의 빈 자리표시자 줄은 치운다. 이름이 기본값이면 프리셋 이름을 가져온다
- 테스트가 보장하는 것: 카탈로그와 리소스 파일 일치, 전부 파싱·검증·라운드트립, 이름 중복 없음, 조건 문장에 연산자 문자 없음

### 5.3 터미널
```
위는 탭 띠 하나: [＋](아이콘만, 툴팁 "새 터미널") [방 탭(도구 로고 + 폴더 이름 + X)…]. 그 아래 도구 줄(방일 때만), 그 아래 본문.
  새 터미널  카드 하나. 안은 두 칸 — 위는 스크롤되는 단계들, 아래는 카드 바닥에 붙는 실행 줄.
             대상은 AI CLI 를 처음 써 보는 일반인이다. 정하는 것은 셋뿐이고 나머지는 다 선택이다 (docs/TERMINAL_CARD_PLAN.md)

             **네 단계, 쌓인다** — 답을 채우면 바로 아래 다음 단계가 열리고("다음" 버튼 없음) 그 단계로 부드럽게 굴러가
             답할 칸에 포커스가 간다. **한 번 열린 단계는 닫히지 않고, 머리는 누르는 곳이 아니다**(Grid, 버튼 아님).
             아직 못 가는 단계는 Opacity 0.4. 머리 왼쪽은 ✓(답함) · ●(지금) · ○(아직)
               ① (필수) 어떤 AI로     카드 3장(ToolIcon 로고 · ToolLook.Title · ToolLook.Vendor · 준비됨/설치 안 됨).
                                     ListView 라 선택 강조·키보드 이동이 테마에 맞는다. XAML 에 도구 이름을 적지 않는다
               ② (필수) 어느 폴더에서  편집형 콤보 하나(FolderChoices = 최근 폴더 + 아는 프로젝트, 중복 제거) + [찾아보기].
                                     미리 채워진 폴더는 [이 폴더로]를 눌러야 답이 된다. 없는 경로면 그 자리에서 말한다
               ③ (필수) 대화를 선택    **콤보 하나.** 첫 줄이 `새로 시작 · 빈 화면에서 출발합니다`(ResumeCandidateViewModel.NewSession),
                                     그 뒤가 이 폴더·도구의 지난 대화(최근 30개, `시각 | 제목 | 주고받음 N회`).
                                     제목에 MaxWidth 를 둔다 — 팝업은 무한 폭으로 재서 긴 제목 하나가 목록을 카드 밖으로 민다
               ④ (선택) 세부 설정      **①~③ 에 답이 다 차야 열린다**(AdvancedExpanded = SessionDone). 답할 것이 없어 ✓ 가 없고
                                     시작 버튼을 막지 않는다. `라벨 72 | 칸` 한 격자에 네 줄:
                                     규칙(편집 + 있음/없음) · 프롬프트 · 모델 · 옵션 인자(도구별 기억)
             앞 단계를 바꿔 ③ 의 답이 무효가 되면 **조용히 지우고 ③ 을 다시 연다** — 빈 단계가 스스로 말한다

             **설명을 화면에 쌓지 않는다.** 회색 잔글씨로 남는 것은 설명이 아니라 **상태**뿐이다
             (모델 읽는 중 · 모델 목록 없음 · 규칙 있음/없음 · 남은 것 한 문장). 나머지는 툴팁으로 간다.
             옵션 인자 아래 **추천(프리셋) 단추는 두지 않는다** — 플래그를 쓸 사람은 칸에 직접 적는다

             **실행 줄(카드 바닥 고정)**: 남은 것 한 문장 → **사람 말 한 문장**(PreviewSentence) →
             **명령 한 줄**(Preview) + [복사] →
             [시작하기 / 이어서 열기 / {도구} 설치](기본) [새 창에서 열기](보조, 설치돼 있을 때만)
             Preview 에는 장식 접두어를 붙이지 않는다 — 복사해서 붙여넣으면 그대로 실행돼야 한다
             미리보기 = resume 인자 + 사용자 인자 + 프롬프트 시작 메시지.
             **프롬프트는 새로/이어서 둘 다에 붙는다** — 세 도구 다 resume 뒤에 첫 메시지를 받는다(help 로 확인, 2026-09-10)
  방        도구 줄(탭 띠 바로 아래, 포토샵식 32px 아이콘만 + 툴팁): 찾기(켜면 입력칸) │ 프롬프트 넣기 · 규칙 편집 │ 폴더 열기 · 같은 폴더로 새 터미널 │ 방 이름
             그 아래 xterm이 남은 높이를 다 쓴다. 페이지에 들어올 때 방이 있으면 마지막 방, 없으면 새 터미널 탭. 마지막 방을 닫으면 새 터미널 탭으로
  안 본 답    다른 탭에 있는 동안 어시스턴트 답이 오면 그 방 탭 로고 모서리에 빨간 점(PulseDot: 고리가 퍼지는 연출 반복)이 켜지고,
             좌측 메뉴 "터미널" 항목에도 빨간 InfoBadge가 붙는다(RoomManager.HasUnseen 집계). 그 탭을 보면 꺼진다
화면 열림 → 도구마다 IProvider.IsInstalledAsync
인자 합치기  TerminalViewModel.ComposeArguments = [resume 인자] [사용자 인자] [프롬프트 시작 메시지]
             프롬프트를 골랐으면 IPromptLibrary.WriteIntoProject(docs/prompts/{id}.md) 후 PromptPresetSerializer.StarterMessage를
             첫 메시지 인자로 붙인다: Claude·Codex는 `"메시지"`(위치 인자), Antigravity는 `-i "메시지"`(= `--prompt-interactive`, §4.5). 메시지 안 큰따옴표는 홑따옴표로
터미널로 열기  설치됨 + WebView2 있음 → 내장 방(아래 "내장 터미널"). 아니면 외부 열기 또는 설치로 폴백
새 창에서 열기 → ITerminalLauncher.LaunchAsync(dir, "claude"|"codex"|"agy", 합친 인자)
설치   폴더 없어도 됨 → ITerminalLauncher.LaunchAsync(dir|home, "npm", "install -g <패키지>")   (IProvider.InstallCommand)
       → 버튼은 "설치 중…"으로 잠기고 3초마다 IsInstalledAsync. 실행 파일이 보이면 "열기"로 돌아온다. 5분이 지나면 지켜보기를 멈추고 안내
```
- 미리보기 줄도 설치 전이면 설치 명령을 그대로 보여준다. 누르면 무엇이 실행되는지 숨기지 않는다
`WindowsTerminalLauncher` 규칙:
- `claude`/`codex`는 npm이 설치한 `.cmd` 셸이다. **항상 셸로 감싼다**: `pwsh -NoExit -Command "& <claude.cmd 절대 경로> <args>"` (pwsh 없으면 `powershell`, 없으면 `cmd /k …`). 인자 안 큰따옴표(첫 메시지)는 PowerShell 경로에서 `\"`로 이스케이프하고 cmd는 그대로 둔다(테스트 있음)
- **이름만 넘기지 않는다.** npm 은 `claude`·`claude.cmd`·`claude.ps1` 을 나란히 깔고, PowerShell 은 이름만 받으면 `.ps1` 을 고른다. Windows 기본 실행 정책(Restricted)은 서명 없는 스크립트를 거부하므로 **새 PC 에서는 터미널 방·새 창 열기가 전부 "claude.ps1 파일을 로드할 수 없습니다" 로 죽는다**(2026-09-11 다른 PC 에서 재현). `.cmd` 는 정책과 무관하게 돈다. 그래서 Claude·Codex 의 `IProvider.LaunchTarget` 은 `ExecutableLocator.NpmLaunchTarget` 으로 PATH 의 `.cmd` 절대 경로를 준다(`ExecutableLocatorTests`). **설치 명령의 `npm` 도 같은 세트라 같은 처리를 한다**(`TerminalViewModel.InstallAsync`) — 띄우기만 고치고 설치를 놓쳐 새 PC 에서 다시 걸렸다 (2026-09-11). 사용자 PC 의 실행 정책을 앱이 바꾸지 않는다 — 그것은 시스템 설정이다. 챗봇 방은 이미 `cmd.exe /c` 로 띄워 영향이 없었다
- `wt.exe`가 PATH에 있으면 `wt -d <dir> <위 셸 명령>`, 없으면 셸을 직접 새 창으로 실행. **wt 없음이 기본 경로**이며 테스트 대상 (Windows 10 Home 기본 상태에 wt 없음 확인)
- Codex는 데스크톱 앱이 설치한 네이티브 exe를 쓰지 않는다. PATH의 npm `codex.cmd`만 사용

**내장 터미널** — 앱을 떠나지 않고 CLI를 xterm 터미널로 띄운다. 방 화면에는 터미널만 보이고, 입력은 xterm이 직접 받는다(별도 입력칸 없음).
- 엔진: `Daiso.Infrastructure.Pty` — `PseudoConsole`(ConPTY: CreatePseudoConsole + 파이프 + STARTUPINFOEX, 자식 생성 동안만 부모 표준 핸들을 비워 콘솔 핸들을 새로 받게 한다. **ConPTY 는 동봉한 `conpty.dll`(NuGet `Microsoft.Windows.Console.ConPTY`, Windows Terminal 의 OpenConsole) 을 먼저 쓰고 없으면 OS 의 kernel32 로 물러난다.** Windows 10 내장 ConPTY 는 대체 화면(`?1049`)·마우스 모드를 터미널에 넘기지 않고 삼킨 뒤 주 화면 N줄을 다시 그려서, Claude Code 처럼 대체 화면에서 그리는 TUI 는 xterm 에 스크롤백이 한 줄도 쌓이지 않았다 — 2026-09-09 측정: 옛 ConPTY 에서는 `buffer=normal, length == rows, mouse=none`, 동봉 ConPTY 로 바꾼 뒤 `buffer=alternate, mouse=any`. 즉 Claude Code 는 대체 화면 + 마우스 추적으로 휠을 직접 받아 자기 기록을 스크롤한다 — Windows Terminal 과 같은 동작이고, 그 위의 옛 출력이 xterm 스크롤백에 남지 않는 것은 Claude Code 의 방식이다. 앱 프로젝트가 `ConptyRequiresx64Host=true`로 `x64\OpenConsole.exe` 복사를 켠다; 패키지 props 는 SDK 가 PlatformTarget 을 정하기 전에 읽혀 스스로 못 켠다), `PtySession`(FileStream IO, 트리 kill 종료, 초기 출력 버퍼링, conhost 마지막 프레임 정착 대기), `PtyEnvironment`(중첩 Claude Code 표식 제거 — 챗봇 엔진도 이걸 쓴다).
- 화면: `Daiso.App.Terminal.TerminalHost` — WebView2 + 동봉 xterm.js(`Assets/xterm`, xterm·fit·search, MIT). `SetVirtualHostNameToFolderMapping`으로 로컬만 로드, 네트워크 없음. 앱↔페이지 메시지: out(base64)·paste·theme·focus·fit·reset·find·find-clear / in·resize·copy·paste·ready(cols,rows)·title. 방을 바꿀 때는 `reset`(clear가 아니라)으로 모드·스크롤백까지 초기화한 뒤 재생하고, 지금 xterm 크기를 그 방 콘솔에 다시 알린다(비활성 중 창 크기가 바뀌어도 따라잡는다). 페이지 메시지 처리는 클립보드 잠김 등 예외를 감싼다(async void). 초기화가 도중에 실패하면 플래그를 되돌려 다음 호출이 다시 시도한다. 설정 `Changed`를 구독해 글자 크기가 열린 방에 즉시 반영된다. 선택 있으면 Ctrl+C 복사·없으면 중단, Ctrl+V·오른클릭 붙여넣기(글자 → 붙임, 복사한 파일 → 경로를 앞뒤 빈칸과 함께 따옴표로 감싸 붙임 — 뒤 빈칸만 두면 CLI가 지워 다음 경로가 들러붙었다, **그림만 있으면 그 방의 도구가 그림을 읽는 키를 넘긴다**: Claude Code는 Windows에서 `Alt+V`(ESC v), Codex·Antigravity는 `Ctrl+V`. 방이 `ToolKind`를 알아 도구별로 나눈다), 파일 끌어놓기 → **셸이 직접 OLE 드롭 대상(`Services/FileDropTarget`, `IDropTarget`+`RegisterDragDrop`)을 창과 자식 창 전부에 등록**해 CF_HDROP 을 받고, 지금 페이지가 `IFileDropSink`면 넘긴다. XAML `AllowDrop`은 이 환경(Windows 10 · unpackaged · WinAppSDK 1.8)에서 어떤 HWND 에도 드롭 대상을 등록하지 않아(측정: `OleDropTargetInterface` 속성 없음 → 금지 커서) 쓰지 않는다. `App` 생성자에서 `OleInitialize`를 부른다. 터미널 페이지는 끄는 동안 `DropOverlay`(히트 테스트 없음, 안내용)를 터미널 위에 덮고, 놓이면 방이 있을 때 `AttachFilesAsync`(그림 파일 → 클립보드 CF_DIB + 도구의 그림 키 → `[Image #n]` 첨부, 나머지 → 경로). 방이 없으면 받지 않는다(폴더를 놓아 프로젝트 폴더를 바꾸는 동작은 넣었다가 뺐다 — 2026-09-09). index.html은 파일 시각을 쿼리로 붙여 캐시된 옛 페이지가 뜨지 않게 한다. 방 도구 줄의 **파일 첨부**(피커, 여러 개) · **입력 비우기**(입력 줄은 CLI 것이라 Ctrl+E·Ctrl+U 키를 보낸다. 여러 줄 입력은 마지막 줄만 지워질 수 있다), F5·Ctrl+1~7은 페이지에서 먹어 앱 단축키와 안 겹치게. 찾기는 방 머리의 찾기 칸(xterm search 애드온): 글이 바뀌면 다음, Enter 다음·Shift+Enter 이전, Esc는 비우고 터미널로 포커스. 터미널 방에서만 보인다.
- 종료는 반드시 프로세스 **트리 전체**를 kill 한다. 루트 셸만 죽이면 자식이 콘솔 출력을 잡아 읽기가 안 풀리고, 파이프 핸들 해제와 네이티브 읽기가 겹쳐 힙이 깨진다. 방 닫기(`TerminalRoomViewModel.Dispose`)는 트리 kill만 UI 스레드에서 바로 하고(앱 종료 때도 고아가 안 남게), 읽기 루프 종료를 기다리는 핸들 정리(`PtySession.Dispose`)는 백그라운드로 보내 UI가 굳지 않게 한다.
- 방 = `TerminalRoomViewModel`(공통 `IRoom` 구현. `IRoom`은 `INotifyPropertyChanged`라 탭이 제목·안 본 점을 따라간다). `RoomManager`(싱글턴)가 방을 들고 있어 화면을 옮겨도 산다. 탭 띠가 `RoomManager.Rooms`(IRoom)를 그리고, 탭을 고르면 하나뿐인 `TerminalHost`를 `BindRoom`으로 그 방에 다시 가리킨다. 출력 버퍼는 `Daiso.Infrastructure.Pty.OutputReplayBuffer`(순수, 테스트 있음): 8MB까지 쌓고, `Attach`가 스냅샷·싱크 교체를 한 잠금 안에서 원자적으로 해 지난 화면을 되돌린다(중복·누락 없음). 세대 번호로 옛 방의 늦은 출력이 새 방에 안 섞이게 한다. 프로세스가 OSC 0 제목을 보내면 방 `Title`(탭 툴팁) 뒤에 붙는다. `TerminalPage`는 `NavigationCacheMode=Required`라 화면을 떠났다 돌아와도 WebView2를 다시 만들거나 버퍼를 재생하지 않는다.
- 열기: 새 터미널 카드의 **"터미널로 열기"** → `PtySession.Start`(셸 명령) + `TerminalRoomViewModel` + 탭 추가 → 그 방 탭으로 전환 → `TerminalHost.BindRoom`. 미설치면 설치 흐름, WebView2가 없으면 외부 터미널(`ITerminalLauncher`)로 폴백. (2026-09-09: "터미널을 앱 안에서 연다" 설정은 뺐다. 새 창 열기 버튼이 따로 있어 스위치가 겹쳤다.) 탭 띠의 "새 터미널"은 카드로 돌아갈 뿐 방을 닫지 않는다.
- 방 도구 줄의 프롬프트: `WriteIntoProject` 뒤 시작 메시지를 `SendRaw`로 터미널 입력에 넣는다(Enter는 사람이 친다). 규칙 편집: `RuleMakerViewModel.ProjectDirectory`에 방 폴더를 넣고 내 규칙 화면으로 간다. 방은 살아 있다.
- 이어서 열기: 세션·요약의 "이어서 열기"가 `TerminalViewModel.PrepareResume(tool, dir, args, autoOpen:true)`로 도구·폴더를 심고 세션 모드를 "기존 세션 이어서"로 두고(목록에서 같은 세션을 찾아 고른다) 터미널로 이동, 화면 로드 때 `ConsumeAutoOpen()`이 도구를 다시 고르고 터미널 방을 연다.
- 앱 종료: 살아 있는 방이 있으면 `AppWindow.Closing`이 한 번 묻고, 계속하면 `App.Rooms.DisposeAll`이 프로세스 트리를 정리한다.
- 설정: 터미널 글자 크기(px).
- **화면 깨짐 방지**: xterm 뷰포트의 스크롤바는 `overflow-y: scroll`로 항상 자리를 잡는다. `auto`면 스크롤백이 생기는 순간 폭이 줄어 fit이 열 수를 바꾸고 ConPTY가 화면을 다시 그려 같은 줄이 두 번 쌓인다. `PtySession.Resize`도 크기가 실제로 바뀔 때만 콘솔에 보낸다.

**챗봇 (B 모드 — 코드만 남기고 UI 비공개)** — 터미널 대신 CLI를 구조화 스트리밍(JSON)으로 다뤄 말풍선 대화로 만드는 코드가 있다. 지금은 **"챗봇으로 열기" 버튼을 빼서 화면에 안 나온다**. 재공개하려면 `TerminalPage`의 그 버튼(`OpenChatbotButton`, `OnOpenChatbotClick`)을 되살린다.
- `ClaudeChatSession`(`claude --print --output-format stream-json --input-format stream-json --include-partial-messages --dangerously-skip-permissions`, stdin/stdout JSON, 트리 kill), `ClaudeStreamParser`(줄 → `ChatEvent`: 파서 테스트로 실제 이벤트 모양 고정), `StreamingRoomViewModel`·`ChatBubbleViewModel`(말풍선, 글자 단위 스트리밍, 도구 호출 카드). 도구 승인은 묻지 않는다. Claude만.
- `SlashCommandReader`(`BuiltInSlashCommands` 내장 명령표 + 디스크의 사용자·프로젝트 명령·스킬, 읽기 전용)는 챗봇 입력의 `/` 선택기용. 터미널 방에서는 안 쓴다.
- 옛 A 방식(채팅 블록·터미널 토글·입력 칸)은 걷어냈다. `TerminalRoomViewModel`에 남은 `SessionTail`은 다른 탭에 있는 동안 어시스턴트 답이 오면 탭에 점을 켜는 용도만이다.

### 5.4 Context Doctor
```
IContextInspector.InspectAsync(tool, dir)
  → provider.ContextFilePatterns(dir) 순서대로 존재 확인·읽기 (UTF-8) → ContextFile[]
  → IContextAnalyzer.Analyze
     · 줄 정규화(trim, 소문자, 연속 공백 1개, 마크다운 불릿 제거) → 해시 → 2개 이상 파일 등장 시 Duplicates
     · 상반 키워드 표(Core 리소스 JSON: 예 "한국어"↔"english", "항상"↔"절대") 매칭 → Conflicts
     · TotalChars = Exists인 파일 Content 길이 합
```

### 5.5 세션 삭제
```
선택 → IFileDisposer.MoveToRecycleBinAsync(sessions)
  → IsActive=true → Skipped("실행 중")
  → 나머지 FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)
  → 인덱스에서 해당 세션 행 삭제
```

### 5.6 CLAUDE.md ↔ AGENTS.md 마이그레이션
```
Plan(projectDir)
  → 프로젝트 루트의 CLAUDE.md / AGENTS.md 읽기 (UTF-8, 없으면 Exists=false)
  → CLAUDE.md의 `@경로` import를 재귀 해석 (깊이 5, 순환 차단, ~ 는 홈)
  → IInstructionMigrator.Plan
       · 양쪽에서 daiso 마커 블록 제거 → Body
       · Body를 줄 단위 LCS diff (왼쪽 CLAUDE.md, 오른쪽 AGENTS.md)
       · 한쪽만 있으면 Suggested = 그 방향, 둘 다 있으면 null → 사용자가 방향 선택

Apply(projectDir, direction, dryRun)
  → Render: 원본 Body 복사
       · 대상이 Codex → `@경로` 줄을 읽어온 내용으로 인라인 전개 + 경고 (Codex는 import 없음)
       · 대상이 Claude → 전개하지 않음
       · 대상 도구의 daiso 블록을 IInstructionMarkerWriter.Apply로 다시 넣는다 (양쪽 각자 형식 유지)
  → dryRun=false면 대상 파일에 UTF-8(BOM 없음) 기록. 내용이 같으면 쓰지 않는다
```
- 원본 파일은 읽기만 한다. 쓰는 것은 **대상 파일 하나**뿐이다
- import 전개는 대상이 Codex일 때만. 실패한 import는 줄을 그대로 두고 경고에 남긴다

### 5.7 로그인 프로필 (계정 백업·전환)

여러 계정을 오가는 사람이 매번 다시 로그인하지 않도록, **지금 로그인 상태를 이름 붙여 보관했다가 되돌린다.**

```
저장  IAuthProfileStore.Save(name, provider, status)
  → provider.AuthFiles 를 읽어 각 파일을 DPAPI(CurrentUser)로 암호화해 보관
  → meta.json 에는 표시용 값만 (도구, 계정 라벨, 이메일, 저장 시각, 재로그인 시각)

전환  IAuthProfileStore.Apply(profile, provider, current)
  → 먼저 현재 파일을 "직전 상태" 프로필로 자동 저장 (되돌릴 수 있게)
     current(지금 AuthStatus)를 함께 넘겨 "직전 상태"에도 계정 이름을 남긴다
  → 프로필의 파일을 복호화해 원래 경로에 바이트 그대로 기록

보관 위치  %LOCALAPPDATA%\d-AI-so\profiles\{tool}\{name}```

- **토큰 값은 meta.json·화면·로그·예외 어디에도 넣지 않는다.** 암호화된 파일 안에만 있다
- DPAPI는 현재 Windows 사용자 계정으로만 풀린다. 파일을 다른 PC로 옮겨도 열리지 않는다
- 인증 파일을 **쓰는 것은 이 흐름뿐이다.** 그 밖의 모든 코드에서 인증 파일은 계속 읽기 전용이다 (§7.1)
- "직전 상태"에도 **어느 계정이었는지 적는다.** 되돌리기가 어디로 가는지 모르면 누를 수 없다
- 전환은 도구가 실행 중이어도 막지 않는다. 다만 이미 떠 있는 세션은 그대로이고, 새로 여는 터미널부터 바뀐다고 화면에서 알린다

---

### 5.8 내 프롬프트 (절차형 프롬프트)

규칙(.daiso)은 매 세션 붙는 제약이고, **프롬프트는 세션의 첫 메시지로 한 번 실행하는 절차**다. 기획 인터뷰, 코드베이스 파악처럼 CLAUDE.md에 넣으면 매번 발동해 버리는 것들을 여기 둔다.

- 파일 형식: 맨 위 `---` 사이 앞머리(`name`, `description`, `category`, `output`) + Markdown 본문. `PromptPresetSerializer`가 읽고 쓴다. 모르는 키는 오류
- 갈래 `PromptCategory`: Planning(기획) · Understanding(파악) · Fixing(수정) · Release(배포)
- 기본 제공: `Daiso.Core/Resources/Prompts/*.md` 임베디드. `BuiltInPrompts`가 카탈로그 순서로 돌려준다. 첫 세트 7개: 기획 `planning-interview`(소규모 프로젝트 기획 인터뷰) · `feature-plan`(기능 하나 추가 계획), 파악 `codebase-tour`(기존 코드베이스 파악), 수정 `bug-repro`(버그 재현과 원인 추적) · `refactor-plan`(리팩터링 계획), 배포 `release-check`(배포 전 점검) · `retro`(작업 회고). 기록형(BUGFIX·RELEASE_CHECK·RETRO)은 파일 끝에 덧붙이고, 계획형은 있으면 덮어쓰지 않고 묻는다
- 내 보관함: `%LOCALAPPDATA%\d-AI-so\prompts\*.md`. `IPromptLibrary`(구현 `PromptLibraryStore`). 기본 제공을 고쳐 저장하면 내 것으로 사본이 생긴다
- **적용 방식**: 본문을 명령줄 인자로 넘기지 않는다(25KB, 길이 한도·인용 문제). 대신 `WriteIntoProject`가 프로젝트의 `docs/prompts/{id}.md`에 **본문만** 쓰고, 시작 메시지 `docs/prompts/{id}.md 파일을 읽고 그 절차대로 진행해 주세요. 결과는 {output} 에 씁니다.`를 만들어 준다. 사용자는 이것을 새 세션의 첫 메시지로 붙인다. 두 도구 공통
- 기본 제공 프롬프트가 지키는 것(테스트가 검사): 코딩 에이전트가 절차 도중 파일을 만들지 않게 막는 문장, 결과 파일 위치 명시, H1 하나, 기획 인터뷰는 첫 회차 질문 정확히 5개
- 터미널 화면의 프롬프트 드롭다운(적용 여부 표시)은 뒤로 미룬 항목이다

## 6. App 구성

- Shell: `NavigationView` 7 항목 → Dashboard(요약), Usage(사용량), Terminal(터미널), Sessions(세션), RuleMaker(내 규칙), Prompts(내 프롬프트), Settings(설정). `Ctrl+1`~`Ctrl+7`
- 페이지별 ViewModel 1개, `ObservableObject` + `RelayCommand`
- DI: `App.xaml.cs`에서 등록. Provider는 `IEnumerable<IProvider>`로 주입
- 설정: `%LOCALAPPDATA%\d-AI-so\settings.json` (최근 폴더, 최근 .daiso, 단가표, 정리 규칙)
- 장시간 작업(인덱싱)은 `IProgress<T>` + `CancellationToken`, UI 스레드 차단 금지

### 6.1 화면 문구 (로컬라이징)

- 사람이 읽는 문구는 **코드에 직접 쓰지 않는다**. 키로만 참조한다
  - XAML: `Text="{loc:Str Key=Dashboard_ToolStatus}"`
  - C#: `UiStrings.Get("...")`, 서식이 있으면 `UiStrings.Format("...", args)`
- 값의 정본은 `src/Daiso.App/Strings/ko-KR/Resources.resw` 하나다. 이 파일은 두 가지로 들어간다
  - `PRIResource` → Windows 리소스(`resources.pri`). 언어 폴더를 추가하면 OS 언어에 따라 골라 쓴다
  - `EmbeddedResource` → 어셈블리에 함께 담아 **폴백**으로 읽는다
- 조회 순서: MRT(`ResourceManager`) → 어셈블리에 담긴 `.resw` 파싱 → 키 문자열.
  unpackaged 실행에서 MRT가 없더라도 화면 문구가 비지 않게 하려는 순서다
- 문체는 **존댓말**로 통일한다 (`~습니다`, `~하세요`). 로그·CLI·문서는 평서체를 그대로 쓴다
- 다른 언어를 넣을 때는 `Strings/<태그>/Resources.resw`를 추가하고 `PRIResource`에 등록하면 된다. 코드는 손대지 않는다


### 6.1 요약 화면의 순서

제목 아래 **탭 띠(`SelectorBar`)**: `전체 · Codex · Claude · Antigravity`. 도구 탭을 고르면 카드·최근 세션·세션 수·용량·최근 7일 토큰이 그 도구로 좁혀진다(토큰은 `ISessionIndex.GetUsageAsync(from, to, tool, ct)`). 파일은 다시 읽지 않고 메모리의 세션 목록과 인덱스만 쓴다.

카드는 **쓰는 빈도** 순으로 놓는다. 요약을 여는 가장 흔한 이유가 "하던 것 이어서 열기"다.

1. 도구 상태 — 지금 로그인돼 있는지. 만료 임박이면 여기서 바로 보인다
2. 최근 세션 — 가장 자주 누르는 것. 접히지 않고 첫 화면에 보여야 한다
3. 세션·사용량 발췌 — 요약은 발췌이므로 **발췌마다 제 화면으로 가는 문**을 둔다 (`세션 보기`, `사용량 보기`)

로그인 프로필(§5.7)은 따로 카드를 갖지 않는다. **각 도구 카드의 `계정` 팝오버** 안에 그 도구의 프로필만 있다.
저장·전환·삭제가 모두 그 안에서 끝나고, 어느 도구인지는 카드가 이미 알고 있어 저장할 때 이름만 묻는다.

### 6.2 화면 공통 규칙

**골격 (2026-09-09, UI_REFACTOR_PLAN §6) — 문서가 아니라 `Daiso.Core.Tests`의 `PageSkeletonTests`가 강제한다.**
MUST 넷을 어긴 페이지를 만들면 테스트가 빨개진다. 규칙을 문서에만 적어 두고 요약·내 규칙에서 빠뜨린 일이 있어서 이렇게 한다.

```
┌─ TitleBar ──────────────────────────────────────────────┐
│ [뒤로][앞으로][☰] 🅳 DAIso                    — □ ✕      │  ← 창 제목줄 (셸)
├─────────────┬───────────────────────────────────────────┤
│ 좌측 메뉴    │  대제목 · 부제           (NavigationView.Header)│  ← 셸이 그린다 (PageHeader)
│             ├───────────────────────────────────────────┤
│             │  [명령 줄]      PageBody.Commands (없으면 사라짐)│
│             │  [필터·탭 줄]   PageBody.Filters  (없으면 사라짐)│  ← 페이지가 그린다
│             │  본문           PageBody.Body                  │     여백 24 · 줄 사이 12
│             │  [푸터]         PageBody.Footer   (없으면 사라짐)│     폭은 Layout 이 정한다
├─────────────┴───────────────────────────────────────────┤
│ 상태바 (인덱싱 진행)                                       │
└──────────────────────────────────────────────────────────┘
```

| | 규칙 | 등급 | 지키는 곳 |
|---|---|---|---|
| 1 | 페이지는 대제목을 그리지 않는다. `IPageHeaderSource.Header`로 알리고 셸이 `NavigationView.Header`에 그린다 | MUST | 테스트: 페이지 XAML에 `PageTitleText` 금지 |
| 2 | 본문 여백 `PagePadding`(24) · 줄 사이 12 한 벌 | MUST | `PageBody`가 정한다 |
| 3 | 본문 폭은 `Layout="Reading"`(`PageMaxWidth` 1280 상한, 왼쪽 정렬) / `Layout="Wide"`(창 전체) 둘뿐 | MUST | 테스트: 두 값 외 거부 |
| 4 | 모든 페이지가 창 하한 1024×700에서 잘림·겹침 없이 들어간다 | MUST | 테스트: `ColumnDefinition MinWidth` 합 + 좌측 메뉴 210 + 여백 48 ≤ 1024 |
| 5 | 세로 스크롤은 한 곳. 머리(제목·명령·필터)와 푸터는 스크롤하지 않는다. Reading 은 본문 한 덩어리, Wide 는 칸마다 | MUST | `PageBody`(Reading) / 페이지(Wide) |
| 6 | 저장은 언제나 푸터(`FooterBorder`) | MUST | 설정·내 규칙·내 프롬프트 |
| 7 | 명령 줄 → 필터 줄 → 본문 → 푸터 순서. 없으면 그 줄이 사라진다 | MUST | 테스트: 루트가 `PageBody` |
| 8 | 2단 화면의 목록 칸은 `ListPaneWidth` 320 / 최소 220 / 최대 600 + `PaneSplitter`. 남는 폭은 오른쪽 칸 | SHOULD | 세션·내 규칙·내 프롬프트 |
| 9 | 도구별 거르기는 탭(`SelectorBar`), 나머지 축은 콤보 | SHOULD | 요약·사용량·세션 |
| 10 | 표·목록의 고정 폭 열은 최소로. 남는 폭은 이름·경로가 먹고 말줄임 + 툴팁 | SHOULD | — |

**창 하한 1024×700의 근거.** 이 앱은 적응형 상태(`AdaptiveTrigger`·`VisualState`)를 만들지 않는다. 개발자용 데스크톱 도구라 좁은 창에서 쓸 일이 거의 없고, 상태마다 검증 비용이 들기 때문이다.
그 대신 **깨지는 구간을 창 하한으로 막는다** (`ShellWindow.MinimumWidth/Height` → `OverlappedPresenter.PreferredMinimum*`). 그러므로 하한에서 깨지는 페이지는 페이지가 틀린 것이고(규칙 4), 좁을 때는 칸을 접는다(내 규칙의 미리보기). 하한 위에서는 유동이다 — Reading 은 1280까지 자라고 그 위로는 왼쪽 정렬, Wide 는 창을 다 쓴다.
검증은 눈이 아니라 캡처로 한다: `tools/shoot-screens.ps1`이 1024 · 1280 · 1600 × 일곱 화면을 찍는다.

**앱 이름과 도구 아이콘 (2026-09-09)**
- 화면·툴팁·설명·창 제목에 보이는 앱 이름은 **DAIso** 하나다. 저장소·실행 파일·설정 폴더 이름(`d-AI-so`)은 그대로 둔다(경로 호환).
- 도구는 글자 배지 대신 **제작사 로고를 원 안에** 그린다: `Controls/ToolIcon`(원 + 로고), 탭·메뉴에는 `ToolLook.LogoIcon`(단색 PathIcon).
  로고 경로는 24×24 — Claude(주황 #D97757)·Codex→OpenAI(초록 #10A37F)는 simple-icons(CC0), Antigravity(파랑→보라→분홍 Google 그라데이션)는
  **대체 마크(위로 향하는 ＾)**다. 자체 로고가 있으나 simple-icons 에 없고(2026-09 확인) 쓸 수 있는 라이선스로 구하지 못했다 —
  Gemini 스파크를 그대로 두면 다른 제품의 상표를 잘못 붙이는 것이라 바꿨다. `ToolLook.LogoPath`가 정본.
- 요약 도구 카드는 로고 원 오른쪽 아래에 로그인 상태 점(초록/노랑/빨강)을 겹친다. 세션 목록·최근 세션·방 탭도 같은 아이콘.
- 도구 모음은 **아이콘만 + 호버 툴팁**(포토샵·게임 엔진 관례): 터미널 방 도구 줄, 세션 목록의 체크 유틸(전체 체크 · 골라 체크 · 휴지통 · 영구 삭제; 목록 카드 머리 오른쪽).
- 알림 점은 빨간색 `Controls/PulseDot`(고리가 퍼지는 반복 연출). 좌측 메뉴는 `CriticalDotInfoBadgeStyle` InfoBadge.
- 사용량 일별 막대는 호버하면 줄 배경이 켜지고 막대가 밝아지며 오른쪽에 내역이 나온다(UsagePage 코드 비하인드).

- **좌 목록 · 우 편집기** 화면(세션, 내 규칙, 내 프롬프트)은 목록 칸을 `ListPaneWidth`(320) / `ListPaneMinWidth`(220) / `ListPaneMaxWidth`(600) 한 벌로 잡고 사이에 `PaneSplitter`를 둔다. 끌어서 왼쪽 판 너비를 바꾸고, 놓으면 `settings.json`의 `PaneWidths[화면]`에 저장돼 다음에 되살아난다(저장된 폭이 기본값을 이긴다). 열의 MinWidth·MaxWidth 안에서만 움직인다. 내 규칙은 명령바의 `목록` 토글로 왼쪽 판을 접을 수 있다(좁은 창용)
- **저장은 언제나 푸터다** (`PageBody.Footer`, `FooterBorder` 스타일: 위 구분선 + 12 여백). 설정·내 규칙·내 프롬프트 셋 다 같다. 페이지 안내 줄(상태·저장 힌트)도 그 푸터 안에 둔다. 상단 명령바에 저장을 두지 않는다 (UI_REFACTOR_PLAN §8.3)
- 내 규칙의 **마크다운 미리보기 칸은 좁으면 저절로 접힌다**. 본문 최소 폭(380) + 미리보기 최소 폭(190) + 간격보다 편집기 칸이 좁으면 접고, 넓어지면 다시 편다(`RuleMakerPage.OnEditorSizeChanged`). 창 하한 1024에서 목록을 펴 두면 이 경우라, `목록`을 접으면 미리보기가 돌아온다. 미리보기 열에는 XAML `MinWidth`를 두지 않는다 — 열이 최소 폭을 요구하면 격자가 제 칸보다 커져 `ActualWidth`가 실제 칸 폭을 말해 주지 않는다
- **긴 목록은 보이는 것만 그린다**: 세션 타임라인처럼 수천 건이 될 수 있는 목록은 `ItemsRepeater`(가상화)로 그리고, 파일 읽기·파싱은 `Task.Run`으로 UI 스레드 밖에서 끝낸 뒤 완성된 목록을 **한 번에** 바인딩한다. 한 건씩 `Add`하지 않는다. 메시지 본문은 1,500자에서 접고 `더 보기`로 편다. 필터 토글은 메모리에서 다시 걸고 파일을 다시 읽지 않는다. 다른 항목을 고르면 앞의 읽기는 `CancellationTokenSource`로 취소한다
- **도구별 거르기는 탭(`SelectorBar`), 나머지 축(프로젝트·기간)은 콤보** (UI_REFACTOR_PLAN §8.4). 요약·사용량·세션이 같은 띠를 쓴다
- **도구 탭 두 꼴**: 화면 전체를 거르는 탭(요약·사용량·세션)은 필터 줄의 독립 띠 `전체 · Codex · Claude · Antigravity`. 카드 하나만 거르는 탭(터미널)은 **그 카드 머리에 붙여** 아래에 구분선을 두고 아이콘을 넣는다. 떠 있는 띠는 어디 것인지 읽히지 않는다
- **화면 루트는 `Controls/PageBody` 하나다.** 슬롯은 `Commands`(명령 줄) → `Filters`(필터·탭 줄) → `Body` → `Footer` 순서고 비운 슬롯은 줄이 사라진다. 여백 `PagePadding`(24), 줄 사이 12. 폭은 `Layout="Reading"`(`PageMaxWidth` 1280 상한, 왼쫁 정렬 — 요약·사용량·설정) / `Layout="Wide"`(창 전체 — 터미널·세션·내 규칙·내 프롬프트) 둘뿐이다. Reading 은 PageBody 가 본문을 스크롤에 담고 머리·푸터는 스크롤하지 않는다. 페이지가 `ScrollViewer`·`MaxWidth`·`Padding`으로 폭과 여백을 직접 정하면 틀린 것이다
- **도구 순서는 `ToolLook.DisplayOrder` = Codex → Claude → Antigravity**. 요약 탭·카드, 터미널 탭, 세션 필터, 컨텍스트 토글이 전부 이 순서다
- **도구 표시는 도구가 내놓는다**: `IProvider.Display`(`ToolDisplay` — 이름·제작사·짧은 이름·배지 한 글자·색·로고 path·표시 순서). Core 에서는 전부 문자열·숫자다(색은 `#RRGGBB`, 로고는 24×24 SVG path) — Core 는 WinUI 를 모른다.
  `ToolLook`은 **값을 들고 있지 않은 조회 창구**다: 등록된 도구에서 `Display`를 찾아 `Color`·`Brush`·`PathIcon`으로 바꿔 준다. 색이 하나면 단색, 둘 이상이면 그라데이션 — 어느 쪽인지 따로 묻지 않는다. 모르는 도구는 `ToolDisplay.Unknown`(id 를 그대로 보여 줌)으로 답한다. 앱이 뜰 때 `ToolRegistry.Refresh()`가 한 번 심는다.
  뷰모델·XAML은 `ToolKind`로 분기하지 않는다. **화면 XAML 에 도구 이름을 적지 않는다** — `ToolNameInXamlTests`가 막는다. 도구 탭은 `SelectorBarVisuals.FillToolTabs`가 `ToolLook.DisplayOrder`로 채운다
- **도구를 늘리는 길은 둘**: (1) 앱에 묻어 두기 — `ToolKind` 상수·`Providers.X` 프로젝트·DI 등록. (2) **빌드 없이** — `%USERPROFILE%\.daiso\tools\*.yaml` 매니페스트(+ 필요하면 세션 어댑터). 자세한 것은 `docs/PLUGIN_PLAN.md`
- **가속기 풍선 숨김**: 셸 루트 격자는 `KeyboardAcceleratorPlacementMode="Hidden"`. 안 그러면 `Ctrl+1` 같은 풍선이 본문 어디에나 뜬다. 단축키는 설정 화면에 적혀 있다
- **저장하지 않은 편집 보호**: 편집기가 더티(`IsDirty` — 마지막 열기·저장·새로 만들기 시점과 직렬화 결과가 다름)이면 다른 목록 항목을 고르거나 새로 만들기·열기·최근 파일을 누를 때 `DiscardDialog`로 묻는다. 취소하면 선택을 이전 항목으로 되돌리고 편집기는 그대로다. 목록을 다시 채우며 같은 항목을 되찾는 것과 방금 저장한 사본을 되찾는 것은 묻지 않는다

- 페이지 구성은 `대제목·부제(셸) → 명령 줄 → 필터 줄 → 본문 → 푸터` 순서로 같다 (`PageBody` 슬롯)
- 본문은 **왼쪽 정렬**, Reading 의 최대 폭은 `PageMaxWidth`(1280). 넓은 창에서 가운데로 뜨지 않는다
- 간격은 4의 배수. 페이지 여백은 `PagePadding`(24, WinUI 권장값), 골격 줄 사이 12, 카드 사이는 16
- 카드는 `CardBorder` 스타일 하나만 쓴다 (모서리 8, 1px 선)
- 글자 스타일은 `PageTitleText` · `PageSubtitleText` · `SectionTitleText` · `MutedText` · `MonoText` · `NumberText` 여섯 개로 제한한다
- 표의 숫자는 `NumberText`(고정 폭·오른쪽 정렬). 큰 수는 `Formats.Tokens`로 줄여 쓰고 원래 값은 ToolTip에 둔다
- 열이 많은 표는 자기 안에서 가로 스크롤한다. 페이지가 잘리게 두지 않는다
- 목록의 한 줄은 **한 줄로 끝낸다** (`TextTrimming`). 전체 값은 ToolTip
- 파괴적인 버튼(삭제)은 대상이 없으면 비활성이다
- 빈 상태는 흰 판을 두지 않고 `NoticeBorder` + `PageSubtitleText` 한 문장(+ 다음에 할 일 버튼) **한 모양**으로 알려 준다. `InfoBar`를 빈 상태에 쓰지 않는다
- **부제는 고정 설명 한 줄이다.** 건수·기간 같은 상태값은 부제에 두지 않고 명령 줄 오른쪽(사용량)이나 푸터(세션·내 규칙·내 프롬프트)에 둔다
- 저장·연동처럼 결과가 파일로 남는 버튼은 **저장할 수 있을 때만 활성**이다 (RuleMaker `CanSave`)
- 사람이 남긴 빈 입력 줄은 저장에서 버린다. 빈 줄 하나로 저장이 막히면 이유를 알기 어렵다
- 저장 실패는 사람 말로 알린다. 줄·열은 **파일을 열다 실패했을 때만** 보여준다
- 조건 트리는 AND · OR만 만든다 (부정은 문장으로 쓴다. REQUIREMENTS §6.3)
- 창은 **1024×700보다 작아지지 않는다** (`OverlappedPresenter.PreferredMinimum*`). 근거는 위 골격 절에. 고정 폭 열은 목록 칸(`ListPaneWidth`) 하나만 두고 나머지는 `*` + `MinWidth`. 좁은 창에서 가운데 열이 짜부라지는 구성을 만들지 않는다
- 자주 쓰지 않는 필터·옵션은 팝오버에 넣어 한 줄이 넘치지 않게 한다
- 왼쪽 메뉴 항목과 페이지 제목은 같은 말을 쓴다. 메뉴 안에 같은 이름의 탭을 또 두지 않는다
- 인덱싱이 끝나면 목록·요약을 자동으로 다시 읽는다. 사람이 "다시 읽기"를 눌러야 최신이 되는 화면을 만들지 않는다
- 단축키는 설정의 "단축키" 카드에 적는다. 알려주지 않는 단축키는 없는 것과 같다
- `SelectorBar` 탭은 페이지 생성자에서 `SelectorBarVisuals.ResetPressedOnLeave`를 붙인다. WinUI 항목은 누른 뒤 포인터가 나가면 회색 눌림이 남는다. 포인터가 나가거나 선택이 바뀌면 `SelectedNormal`/`UnselectedNormal`로 되돌린다
- **페이지는 대제목을 그리지 않는다. 셸이 그린다.** 페이지는 `IPageHeaderSource.Header`(`Controls/PageHeader` — 제목 키 + 고정 부제, 또는 `Follow`로 뷰모델 속성을 따라가는 부제)를 내놓고, `ShellWindow`가 `Frame.Navigated`에서 `NavigationView.Header`에 싣는다. 그리는 모양은 셸의 `HeaderTemplate` 하나다(`PageTitleText` + `PageSubtitleText`). 페이지 XAML에 `PageTitleText`가 나오면 틀린 것이다. 기간 콤보·다시 읽기·검색·명령바 같은 화면 도구는 본문 첫 줄이다. 모든 페이지가 대제목을 가진다 (터미널 포함)
- **뒤로/앞으로는 셸 것이다.** 제목줄(`TitleBar` 컨트롤)에 있다 — 뒤로는 내장 버튼(`IsBackButtonVisible`/`BackRequested`), 앞으로와 메뉴 접기(☰)는 `LeftHeader` 슬롯. 마우스 엄지 버튼(XButton1 뒤로 / XButton2 앞으로)도 같은 `NavigationHistory`를 쓴다. 페이지마다 만들지 않고, 본문 위에 겹쳐 그리지도 않는다 (UI_REFACTOR_PLAN §8.1)
- **이력은 한 줄기다.** `NavigationHistory`(순수 로직, UI를 모른다)가 자리(`NavigationSpot` = 좌측 메뉴 페이지 + 도구 탭 + 터미널 방)를 브라우저처럼 쌓는다. 새 자리로 옮기면 앞으로 갈 곳은 지워지고, 되돌리는 동안(`Restoring`)에는 기록하지 않는다. 방을 닫으면 그 방을 가리키는 자리를 `Forget`으로 걷어낸다 — 안 그러면 뒤로가기가 없어진 방으로 간다
- **탭 띠는 양방향이어야 한다.** 사람이 누른 것만 뷰모델로 보내면(단방향) 뒤로/앞으로처럼 뷰모델 쪽에서 탭이 바뀔 때 띠 표시가 어긋난다. `SelectorBarVisuals.Select`로 뷰모델 → 띠도 맞춘다 (페이지 `Loaded`와 뷰모델 `PropertyChanged` 둘 다에서)
- 입력 칸이 있는 페이지는 생성자에서 `FocusRelease.Attach(this)`를 붙인다. WinUI는 빈 자리를 눌러도 포커스를 옮기지 않아 커서가 입력 칸에 남는다. 포커스를 받을 컨트롤이 없는 곳을 누르면 **크기 0인 싱크 버튼**이 받아 놓아 준다. 스크롤이 맨 위로 튀지 않으려면 세 가지를 지켜야 한다 — (1) 싱크는 **잎 컨트롤**이어야 한다. `Page`는 `ContentControl`이라 포커스가 안쪽 첫 입력 칸으로 흘러내린다(`page.Focus()`는 True를 돌려주지만 ~90ms 뒤 맨 위 TextBox로 옮겨간다). (2) 싱크는 **`ScrollViewer` 바깥**, 페이지 루트 패널에 둔다. 안에 있으면 포커스가 스크롤을 끌고 다닌다. (3) **누를 때와 뗄 때 둘 다** 처리한다. 누를 때만 하면 WinUI가 뗄 때 빈 배경 클릭을 처리하며 포커스를 `ScrollViewer` 안 첫 포커스 가능 요소로 밀어넣어 우리 처리를 덮어쓴다(누름 88ms 뒤 sink → SessionHomeBox, 직후 BringIntoView로 offset 626 → 125)
- 단축키가 있는 버튼의 툴팁에는 키를 적는다 (`다시 읽기 (F5)`, `Ctrl+S`). 가속기 풍선을 숨겼으므로(`KeyboardAcceleratorPlacementMode=Hidden`) 툴팁이 유일한 안내다. 설정의 단축키 카드에 적힌 키는 그 화면 전부에서 실제로 동작해야 한다 (F5는 요약·사용량·세션)
- 잠긴(IsEnabled=false) 컨트롤은 왜 잠겼는지 근처 글로 말한다. 잠긴 컨트롤은 마우스를 받지 않아 툴팁이 안 뜬다. 이유를 툴팁으로 줘야 하면 `Background=Transparent`인 Border로 감싸 그쪽에 단다(세션 프로젝트 콤보). 이유가 화면에 이미 있으면(선택 0건 알약, 왼쪽에서 고르라는 안내) 그대로 둔다. 상태 줄로 말할 수도 있다(내 규칙 저장 힌트, 내 프롬프트 기본 제공 안내)
- 잘려 보일 수 있는 글(`TextTrimming=CharacterEllipsis`)에는 전체 문구 툴팁을 단다. 머리글자·아이콘만 있는 것에는 이름 툴팁을 단다
- 글자는 12px보다 작게 쓰지 않는다. 보조 색(`TextFillColorSecondaryBrush`) 글자는 12px가 하한이다. 색 원 위의 흰 글자는 4.5:1 이상 (ToolLook 색은 이 기준으로 골랐다)
- 비어 있음 안내에는 다음에 할 일을 한 문장 붙인다 ("필터를 넓혀 보세요", "기간을 넓히거나 전체 탭을 보세요"). 읽는 동안은 페이지 위 `ProgressBar`(요약·사용량) 또는 필터 줄의 `ProgressRing`(세션)

**테마** — 설정의 테마는 고른 즉시 적용한다 (`SettingsViewModel.ThemeChanged` → `ShellWindow.ApplyTheme`).
- `System`: `MicaBackdrop` + 배경 없음. OS 테마를 따른다
- `Light` / `Dark`: Mica는 OS 테마 색으로 남아 글자와 어긋나므로 **끄고** 그 테마의 단색으로 칠한다
- 제목줄은 `ExtendsContentIntoTitleBar` + WinUI `TitleBar` 컨트롤(`SetTitleBar(AppTitleBar)`)로 앱이 직접 그린다. Windows 10은 제목줄 색 API를 지원하지 않아 시스템이 그리면 밝은 띠가 남는다. `TitleBar`가 끌기 영역과 버튼의 입력 통과 영역을 스스로 맞추므로 `InputNonClientPointerSource`를 손으로 만지지 않는다. `NavigationView`의 뒤로·햄버거는 숨긴다(중복)
---

## 7. 불변 규칙

1. 인증 토큰·API 키 값은 어떤 로그·화면·파일·예외 메시지에도 쓰지 않는다. JWT는 `exp` 숫자만 추출하고 원문은 즉시 버린다.
2. CLI가 만든 파일(`.credentials.json`, `.claude.json`, `auth.json`, 세션 jsonl)은 **읽기 전용**. 삭제는 세션 jsonl만, 기본 휴지통, 실행 중 세션은 거부.
3. `CLAUDE.md` / `AGENTS.md` 수정은 `<!-- daiso:start -->` ~ `<!-- daiso:end -->` 블록 안으로만. 블록 밖은 바이트 불변.
4. 네트워크 호출 없음.
5. Core는 경로·파일·프로세스·환경변수를 다루지 않는다 (§1 순수성 규칙).
6. 모든 텍스트 파일 IO는 UTF-8 명시.

---

## 8. 테스트 전략

| 프로젝트 | 대상 | 방식 |
|---|---|---|
| Core.Tests | RulePresetSerializer | 샘플 라운드트립(문자열 비교), 검증 규칙 표 각 행 1케이스 이상, 오류 line/column |
| Core.Tests | MarkdownRuleRenderer | 스냅샷. 괄호 규칙 케이스: `A & (B \| C)`, `(A & B) \| C`, `!(A & B)`, `!!A`, 특수문자 리프 |
| Core.Tests | InstructionMarkerWriter | null/마커 없음/마커 있음, CRLF·LF 각각, 블록 밖 바이트 동일 |
| Core.Tests | ContextAnalyzer | 인메모리 ContextFile로 중복·충돌 검출 |
| Providers.Tests | Claude 파서 | fixture: 문자열 user, text 배열 user, tool_result user, isMeta, isSidechain, synthetic 모델. 카운트·usage·FirstPrompt 기대값 |
| Providers.Tests | Codex 파서 | fixture 2종(구형 user_message / 신형 item_completed). world_state 포함해 인덱스 오염 없음 확인. token_count 마지막 값, info null 건너뜀 |
| Providers.Tests | Auth | fixture 인증 파일(더미 토큰 `DUMMY_TOKEN_xxx`). 상태 3종. `JsonSerializer.Serialize(status)`에 `DUMMY_TOKEN` 미포함 |
| Providers.Tests | 스트리밍 | 20MB 생성 fixture를 `ReadMessagesAsync`로 순회. `GC.GetTotalMemory` 피크가 파일 크기의 2배 미만 (`Trait Slow`) |
| Infrastructure.Tests | SqliteSessionIndex | 임시 DB. Rebuild → List/Search(trigram 부분 문자열, 2글자 폴백)/Usage. Refresh 증분: 가짜 IProvider로 append 후 offset 이어 읽기 호출 인자 검증 |
| Infrastructure.Tests | WindowsTerminalLauncher | 프로세스 실행 대신 명령 문자열 생성 함수를 분리해 wt 유/무, pwsh/powershell/cmd 폴백 문자열 검증 |
| Infrastructure.Tests | RecycleBinFileDisposer | IsActive 세션 Skipped 확인 (실제 삭제는 임시 파일로 1건) |
| App | 수동 | — |

fixture 규칙: 실제 파일에서 구조만 발췌, 경로는 `C:\Fixture\Project`, 프롬프트는 `더미 질문 N`, 토큰은 `DUMMY_TOKEN_*`.

### SearchablePicker — 고르는 칸 하나로 모은다 (2026-09-11)

`src/Daiso.App/Controls/SearchablePicker.cs`. 누르거나 포커스가 오면 **맨 위에 검색칸이 붙은 목록**이 열리고,
목록은 `VisibleItemCount`(기본 5)줄까지만 보이고 그 이상은 안에서 스크롤한다.

| 속성 | 뜻 |
|---|---|
| `ItemsSource` · `SelectedItem` · `ItemTemplate` | 콤보박스와 같다. 고른 것도 같은 틀로 그린다 |
| `PlaceholderText` · `SearchPlaceholder` | 칸이 비었을 때 / 검색칸의 자리표시자 |
| `VisibleItemCount` | 한 번에 보일 줄 수(기본 5). 줄 높이는 40 고정 |
| `AllowFreeText` | 목록에 없는 것도 적어 쓸 수 있는가. Enter 로 `FreeTextSubmitted` |
| `SelectionChanged` · `FreeTextSubmitted` | "값이 바뀐 것"이 아니라 "사람이 고른 것"만 온다 |

검색은 항목의 `ToString()` 으로 거른다 — 목록에 쓰는 뷰모델은 사람이 읽는 글을 `ToString()` 으로 내놓아야 한다.

**콤보박스를 안 쓰는 이유**: WinUI 콤보 팝업에는 검색칸을 넣을 자리가 없고, 편집형 콤보는 겉이 그냥 입력 칸이라
"고를 것이 있다"는 사실이 화살표 하나에 걸린다. 목록이 서른 줄이 되면 훑어 고르는 것이 불가능해진다.
쓰는 곳: 새 터미널 카드의 `폴더`(AllowFreeText) · `대화` · `프롬프트` · `모델`, 세션 화면의 `프로젝트`.

**어디까지 쓰는가.** 목록이 <b>길거나 늘어나는 칸</b>에만 쓴다. 갈래·기간·테마·MUST/SHOULD/MAY 처럼
<b>서너 개로 고정된 칸은 그냥 `ComboBox`</b> 다 — 세 줄짜리 목록 위에 검색칸을 얹으면 고르는 일이 더 번거로워진다.

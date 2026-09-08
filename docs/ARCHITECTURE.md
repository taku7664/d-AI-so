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
└──────────────────────┴──────────────────────┘
```

의존 방향: `App → Core ← Providers`, `App → Core ← Infrastructure`, `Infrastructure → Providers`(세션 인덱스가 Provider를 사용).
`Providers.Claude`/`Providers.Codex` → `Providers.Common`. Common은 Core만 참조한다 (§4.3 공용 코드가 Core 순수성 규칙을 지킬 수 없어 별도 어셈블리로 둔다).
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
public enum ToolKind { Claude, Codex }

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
}

public sealed record AuthFile(string Path, bool Required);
```

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

모든 파일 읽기는 **UTF-8 명시** (`new StreamReader(path, Encoding.UTF8)`). 시스템 로케일(CP949)에 의존하지 않는다.

### 4.1 Claude (`Daiso.Providers.Claude`)

**인증**
- `%USERPROFILE%\.claude\.credentials.json` → `claudeAiOauth`
  - `refreshTokenExpiresAt` (ms epoch) → **`SessionExpiresAt`**. `expiresAt`은 자동 갱신되는 단기 액세스 토큰이라 상태 판정에 쓰지 않는다 (확인: 액세스 토큰 만료 후에도 CLI 정상 동작)
  - `subscriptionType`, `rateLimitTier`, `scopes` → Extras
  - `mcpOAuth` 키 이름(`|` 앞부분)만 → Extras
- `%USERPROFILE%\.claude.json` → `oauthAccount.{emailAddress, displayName, organizationName}` → `Email`, `AccountLabel`. 파일이나 키가 없으면 null

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

### 4.3 경로 정규화 (`ProjectPathNormalizer`, `Daiso.Providers.Common`)
- `Path.GetFullPath` 후 드라이브 문자 대문자, 끝 구분자 제거, `\` 통일
- 그룹핑·필터·OrphansOnly 판정은 정규화 값으로만

### 4.4 Context 파일 목록 (`ContextFilePatterns`, 로드 순서대로)

| Claude | Codex |
|---|---|
| `~/.claude/CLAUDE.md` | `~/.codex/AGENTS.md` |
| 루트→상위 방향: `{ancestor}/CLAUDE.md`, `{ancestor}/.claude/CLAUDE.md` (드라이브 루트까지) | git 루트→cwd 방향: `{dir}/AGENTS.md` |
| `{dir}/CLAUDE.md`, `{dir}/.claude/CLAUDE.md`, `{dir}/CLAUDE.local.md` | `{dir}/AGENTS.md` |
| `{dir}/.claude/rules/*.md` | — |
| `{dir}/PROJECT_RULES.daiso` | `{dir}/PROJECT_RULES.daiso` |

---

## 5. 데이터 흐름

### 5.1 세션 인덱싱 (증분)
```
RefreshAsync
  → 각 IProvider.EnumerateSessionsAsync  (파일 경로, size, mtime만)
  → sessions 테이블의 (size, mtime, last_offset) 와 비교
     · 신규          → offset 0부터 ReadMessagesAsync
     · size 증가     → last_offset부터 이어 읽기 (jsonl은 append-only)
     · size 감소/변경 → offset 0부터 재파싱 (재작성된 경우)
     · 동일          → 건너뜀
  → messages_fts 삽입, usage_daily 갱신, last_offset = 파일 끝
```
- SQLite: `%LOCALAPPDATA%\d-AI-so\index.db`
- **읽기와 쓰기는 연결을 나눈다.** 목록·검색·사용량은 호출마다 새 연결을 열고, 갱신·재구축은 전용 연결 + 세마포어로 직렬화한다.
  하나의 연결을 화면과 배경 갱신이 같이 쓰면 리더가 겹쳐 `IndexOutOfRange`로 깨진다 (WAL이라 읽기는 쓰기를 기다리지 않는다)
- `messages_fts`: FTS5, **`tokenize='trigram'`**. 3글자 미만 검색어는 `LIKE` 폴백
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

### 5.3 터미널
```
폴더 선택 + 도구 선택 (+ 세션 → BuildResumeArguments)
  → ITerminalLauncher.LaunchAsync(dir, "claude"|"codex", args)
```
`WindowsTerminalLauncher` 규칙:
- `claude`/`codex`는 npm이 설치한 `.cmd` 셸이다. **항상 셸로 감싼다**: `pwsh -NoExit -Command "& claude <args>"` (pwsh 없으면 `powershell`, 없으면 `cmd /k claude <args>`)
- `wt.exe`가 PATH에 있으면 `wt -d <dir> <위 셸 명령>`, 없으면 셸을 직접 새 창으로 실행. **wt 없음이 기본 경로**이며 테스트 대상 (Windows 10 Home 기본 상태에 wt 없음 확인)
- Codex는 데스크톱 앱이 설치한 네이티브 exe를 쓰지 않는다. PATH의 npm `codex.cmd`만 사용

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

## 6. App 구성

- Shell: `NavigationView` 6 항목 → Dashboard(요약), Usage(사용량), Terminal(터미널), Sessions(세션), RuleMaker(규칙), Settings(설정)
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

카드는 **쓰는 빈도** 순으로 놓는다. 요약을 여는 가장 흔한 이유가 "하던 것 이어서 열기"다.

1. 도구 상태 — 지금 로그인돼 있는지. 만료 임박이면 여기서 바로 보인다
2. 최근 세션 — 가장 자주 누르는 것. 접히지 않고 첫 화면에 보여야 한다
3. 세션·사용량 발췌 — 요약은 발췌이므로 **발췌마다 제 화면으로 가는 문**을 둔다 (`세션 보기`, `사용량 보기`)
4. 로그인 프로필 — 계정을 오갈 때만 쓴다. 접어 두고 머리글에 개수만 보인다

### 6.2 화면 공통 규칙

- 페이지 구성은 `제목 → (부제) → 행동 버튼 → 카드들` 순서로 같다
- 본문은 **왼쪽 정렬**, 최대 폭 `ContentMaxWidth`(1280). 넓은 창에서 가운데로 뜨지 않는다
- 간격은 4의 배수. 페이지 여백은 `PagePadding`, 카드 사이는 16
- 카드는 `CardBorder` 스타일 하나만 쓴다 (모서리 8, 1px 선)
- 글자 스타일은 `PageTitleText` · `PageSubtitleText` · `SectionTitleText` · `MutedText` · `MonoText` · `NumberText` 여섯 개로 제한한다
- 표의 숫자는 `NumberText`(고정 폭·오른쪽 정렬). 큰 수는 `Formats.Tokens`로 줄여 쓰고 원래 값은 ToolTip에 둔다
- 열이 많은 표는 자기 안에서 가로 스크롤한다. 페이지가 잘리게 두지 않는다
- 목록의 한 줄은 **한 줄로 끝낸다** (`TextTrimming`). 전체 값은 ToolTip
- 파괴적인 버튼(삭제)은 대상이 없으면 비활성이다
- 빈 상태는 흰 판을 두지 않고 `NoticeBorder`로 다음에 할 일을 알려 준다
- 저장·연동처럼 결과가 파일로 남는 버튼은 **저장할 수 있을 때만 활성**이다 (RuleMaker `CanSave`)
- 사람이 남긴 빈 입력 줄은 저장에서 버린다. 빈 줄 하나로 저장이 막히면 이유를 알기 어렵다
- 저장 실패는 사람 말로 알린다. 줄·열은 **파일을 열다 실패했을 때만** 보여준다
- 조건 트리는 AND · OR만 만든다 (부정은 문장으로 쓴다. REQUIREMENTS §6.3)
- 창은 **1024×700보다 작아지지 않는다** (`OverlappedPresenter.PreferredMinimum*`). 목록·상세를 나란히 두는 화면이라 그 아래로는 못 쓴다
- 고정 폭 열은 하나만 둔다 (상세 400). 나머지는 `*` + `MinWidth`. 좁은 창에서 가운데 열이 짜부라지는 구성을 만들지 않는다
- 자주 쓰지 않는 필터·옵션은 팝오버에 넣어 한 줄이 넘치지 않게 한다
- 왼쪽 메뉴 항목과 페이지 제목은 같은 말을 쓴다. 메뉴 안에 같은 이름의 탭을 또 두지 않는다
- 인덱싱이 끝나면 목록·요약을 자동으로 다시 읽는다. 사람이 "다시 읽기"를 눌러야 최신이 되는 화면을 만들지 않는다
- 단축키는 설정의 "단축키" 카드에 적는다. 알려주지 않는 단축키는 없는 것과 같다

**테마** — 설정의 테마는 고른 즉시 적용한다 (`SettingsViewModel.ThemeChanged` → `ShellWindow.ApplyTheme`).
- `System`: `MicaBackdrop` + 배경 없음. OS 테마를 따른다
- `Light` / `Dark`: Mica는 OS 테마 색으로 남아 글자와 어긋나므로 **끄고** 그 테마의 단색으로 칠한다
- 제목줄은 `ExtendsContentIntoTitleBar`로 앱이 직접 그린다. Windows 10은 제목줄 색 API를 지원하지 않아 시스템이 그리면 밝은 띠가 남는다
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

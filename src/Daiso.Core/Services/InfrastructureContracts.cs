namespace Daiso.Core;

/// <summary>.daiso 파일 읽기·쓰기와 지시문 연동. 구현은 Daiso.Infrastructure. (ARCHITECTURE §3.3)</summary>
public interface IRuleFileService
{
    RulePreset Load(string path);

    void Save(RulePreset preset, string path);

    /// <summary>프로젝트의 도구별 지시문 파일에 daiso 마커 블록을 넣거나 갱신한다.</summary>
    void EnsureInstruction(string projectDir, IEnumerable<IProvider> providers);
}

/// <summary>
/// CLAUDE.md ↔ AGENTS.md 마이그레이션에 파일 IO를 붙인다. 구현은 Daiso.Infrastructure. (ARCHITECTURE §5.6)
/// </summary>
public interface IInstructionMigrationService
{
    /// <summary>프로젝트 루트의 두 파일을 읽어 비교한다. import는 재귀 해석한다.</summary>
    InstructionMigrationPlan Plan(string projectDir);

    /// <summary>고른 방향으로 대상 파일 하나만 쓴다. <paramref name="dryRun"/>이면 내용만 만들고 쓰지 않는다.</summary>
    MigrationResult Apply(string projectDir, MigrationDirection direction, bool dryRun);
}

/// <summary>
/// 로그인 상태를 이름 붙여 보관하고 되돌린다. (ARCHITECTURE §5.7)
/// 구현은 Daiso.Infrastructure. 파일 내용은 이 PC의 사용자 계정으로만 풀리게 암호화해 둔다.
/// </summary>
public interface IAuthProfileStore
{
    /// <summary>보관 중인 프로필. 최근에 저장한 것이 앞.</summary>
    IReadOnlyList<AuthProfile> List();

    /// <summary>지금 로그인 상태를 이름 붙여 저장한다. 같은 이름이면 덮어쓴다.</summary>
    AuthProfile Save(string name, IProvider provider, AuthStatus status);

    /// <summary>
    /// 프로필을 현재 자리로 되돌린다. 되돌리기 전에 지금 상태를 자동으로 보관한다.
    /// </summary>
    void Apply(AuthProfile profile, IProvider provider);

    /// <summary>프로필을 지운다.</summary>
    void Remove(AuthProfile profile);
}

/// <summary>세션 파일 정리. 실행 중 세션은 거부한다. (ARCHITECTURE §5.5)</summary>
public interface IFileDisposer
{
    Task<DisposeResult> MoveToRecycleBinAsync(IEnumerable<SessionInfo> sessions);

    Task<DisposeResult> DeletePermanentlyAsync(IEnumerable<SessionInfo> sessions);
}

public sealed record DisposeResult(
    IReadOnlyList<string> Deleted,
    IReadOnlyList<(string Path, string Reason)> Skipped);

/// <summary>터미널 창에서 도구를 실행한다. (ARCHITECTURE §5.3)</summary>
public interface ITerminalLauncher
{
    Task LaunchAsync(string workingDir, string command, string arguments);
}

/// <summary>컨텍스트 파일을 읽어 <see cref="IContextAnalyzer"/>에 넘긴다. (ARCHITECTURE §5.4)</summary>
public interface IContextInspector
{
    Task<ContextReport> InspectAsync(ToolKind tool, string projectDir, CancellationToken ct);
}

/// <summary>세션을 마크다운으로 내보낸다.</summary>
public interface ISessionExporter
{
    Task ExportMarkdownAsync(
        SessionInfo session,
        string outputPath,
        ExportOptions options,
        CancellationToken ct);
}

public sealed record ExportOptions(
    bool IncludeToolCalls = true,
    bool IncludeSystem = false,
    bool IncludeSidechain = false)
{
    public static readonly ExportOptions Default = new();
}

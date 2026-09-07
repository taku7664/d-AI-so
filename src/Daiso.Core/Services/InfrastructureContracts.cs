namespace Daiso.Core;

/// <summary>.daiso 파일 읽기·쓰기와 지시문 연동. 구현은 Daiso.Infrastructure. (ARCHITECTURE §3.3)</summary>
public interface IRuleFileService
{
    RulePreset Load(string path);

    void Save(RulePreset preset, string path);

    /// <summary>프로젝트의 도구별 지시문 파일에 daiso 마커 블록을 넣거나 갱신한다.</summary>
    void EnsureInstruction(string projectDir, IEnumerable<IProvider> providers);
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

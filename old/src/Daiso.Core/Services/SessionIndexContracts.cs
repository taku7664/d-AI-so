namespace Daiso.Core;

/// <summary>세션 인덱스. 구현은 Daiso.Infrastructure에 있다. (ARCHITECTURE §3.3)</summary>
public interface ISessionIndex
{
    /// <summary>인덱스를 처음부터 다시 만든다.</summary>
    Task RebuildAsync(IProgress<IndexProgress> progress, CancellationToken ct);

    /// <summary>바뀐 파일만 이어 읽어 갱신한다. (ARCHITECTURE §5.1)</summary>
    Task RefreshAsync(CancellationToken ct);

    Task<IReadOnlyList<SessionInfo>> ListAsync(SessionFilter filter, CancellationToken ct);

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);

    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>같은 요약을 도구 하나로 좁혀서. <paramref name="tool"/>이 null이면 전체와 같다. (요약 화면의 도구 탭)</summary>
    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, ToolKind? tool, CancellationToken ct);
}

public sealed record IndexProgress(int Done, int Total, string CurrentFile);

public sealed record SessionFilter(
    ToolKind? Tool,
    string? ProjectPath,
    DateOnly? From,
    DateOnly? To,
    bool OrphansOnly,
    long? MinSizeBytes,
    bool IncludeArchived = true)
{
    /// <summary>아무 조건도 걸지 않은 필터.</summary>
    public static readonly SessionFilter All = new(null, null, null, null, false, null);
}

public sealed record SearchHit(SessionInfo Session, SessionMessage Message, string Snippet);

public sealed record UsageSummary(
    IReadOnlyList<UsageDay> Days,
    IReadOnlyDictionary<string, TokenUsage> ByProject,
    IReadOnlyDictionary<string, TokenUsage> ByModel);

public sealed record UsageDay(DateOnly Date, TokenUsage Usage);

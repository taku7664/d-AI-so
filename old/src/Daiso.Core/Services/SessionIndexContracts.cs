namespace Daiso.Core;

/// <summary>세션 인덱스. 구현은 Daiso.Infrastructure에 있다. (ARCHITECTURE §3.3)</summary>
public interface ISessionIndex
{
    /// <summary>인덱스를 처음부터 다시 만든다.</summary>
    Task RebuildAsync(IProgress<IndexProgress> progress, CancellationToken ct);

    /// <summary>바뀐 파일만 이어 읽어 갱신한다. (ARCHITECTURE §5.1)</summary>
    Task RefreshAsync(CancellationToken ct);

    /// <summary>
    /// 이 파일들만 <see cref="RefreshAsync"/> 와 같은 규칙으로 이어 읽는다. 없어진 파일은 인덱스에서 뺀다.
    /// 도구가 파일 하나의 메타를 못 주면(<see cref="IProvider.ReadSessionMetaAsync"/> 가 null) 그 파일은 건너뛴다.
    /// 세션 폴더를 지켜보다 바뀐 파일만 다시 읽을 때 쓴다. 전체 갱신은 모든 세션 파일의 앞부분을 다시 읽는다.
    /// </summary>
    Task RefreshFilesAsync(IReadOnlyCollection<string> filePaths, CancellationToken ct);

    Task<IReadOnlyList<SessionInfo>> ListAsync(SessionFilter filter, CancellationToken ct);

    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);

    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>같은 요약을 도구 하나로 좁혀서. <paramref name="tool"/>이 null이면 전체와 같다. (요약 화면의 도구 탭)</summary>
    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, ToolKind? tool, CancellationToken ct);

    /// <summary>
    /// 같은 요약을 도구와 프로젝트로 좁혀서. <paramref name="projectPath"/>가 null이면 모든 프로젝트다.
    /// 경로는 대소문자를 가리지 않고 비교한다. (새 앱 사용량 화면의 "이 프로젝트")
    /// </summary>
    Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, ToolKind? tool, string? projectPath, CancellationToken ct);

    /// <summary>
    /// 세션 하나의 사람 메시지를 최근 것부터 <paramref name="limit"/>개. 인덱스에 담은 본문만 본다(파일을 열지 않는다).
    /// 도구가 사람 자리에 끼워 넣은 줄(알림·요약 이어 붙이기 같은 것)도 그대로 돌려준다. 거르는 것은 부르는 쪽이다. (새 앱 요약의 "마지막 질문")
    /// </summary>
    Task<IReadOnlyList<SessionMessage>> GetLatestUserMessagesAsync(string filePath, int limit, CancellationToken ct);
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

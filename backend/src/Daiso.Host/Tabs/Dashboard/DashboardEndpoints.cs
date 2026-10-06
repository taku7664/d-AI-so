using Daiso.Core;
using Daiso.Host.Services;
using Daiso.Host.Shared;
using Daiso.Host.Tabs.Sessions;
using Daiso.Host.Tabs.Usage;
using Daiso.Providers.Manifest;

namespace Daiso.Host.Tabs.Dashboard;

/// <summary>숫자 셋.</summary>
/// <param name="Sessions">세션 수.</param>
/// <param name="SizeBytes">세션 파일 합.</param>
/// <param name="Last7Days">최근 7일 토큰.</param>
public sealed record DashboardStats(int Sessions, long SizeBytes, UsageTokens Last7Days);

/// <summary>"손볼 것" 한 줄. 화면이 <c>Kind</c> 를 보고 문장과 단추를 고른다.</summary>
/// <param name="Kind"><c>account</c>(로그인 곧 만료·만료·없음) · <c>cleanup</c>(정리할 만한 세션) · <c>index</c>(인덱스 실패) · <c>plugin</c>(도구 플러그인 실패).</param>
/// <param name="Level"><c>warn</c> · <c>bad</c>.</param>
/// <param name="Tool">account 일 때 도구 id.</param>
/// <param name="State">account 일 때 상태(<c>expiringSoon</c> · <c>expired</c> · <c>missing</c>).</param>
/// <param name="At">account 일 때 만료 시각.</param>
/// <param name="Count">cleanup 일 때 세션 수.</param>
/// <param name="Bytes">cleanup 일 때 크기 합.</param>
/// <param name="Name">plugin 일 때 플러그인 이름.</param>
/// <param name="Detail">index·plugin 일 때 까닭.</param>
public sealed record Attention(
    string Kind,
    string Level,
    string? Tool = null,
    string? State = null,
    DateTimeOffset? At = null,
    int? Count = null,
    long? Bytes = null,
    string? Name = null,
    string? Detail = null);

/// <summary>"모든 프로젝트" 요약의 프로젝트 카드.</summary>
/// <param name="Path">프로젝트 경로.</param>
/// <param name="Label">보여 줄 이름. 이름이 겹치면 상위 폴더까지 붙인다.</param>
/// <param name="Exists">폴더가 아직 있는가.</param>
/// <param name="Sessions">세션 수.</param>
/// <param name="SizeBytes">세션 파일 합.</param>
/// <param name="LastActivity">마지막 세션이 바뀐 때.</param>
public sealed record ProjectCard(string Path, string Label, bool Exists, int Sessions, long SizeBytes, DateTimeOffset? LastActivity);

/// <summary>요약 화면.</summary>
/// <param name="Project">지금 프로젝트 경로. null 이면 모든 프로젝트.</param>
/// <param name="Recent">최근 세션 5개.</param>
/// <param name="Stats">숫자.</param>
/// <param name="Attention">손볼 것.</param>
/// <param name="Projects">모든 프로젝트일 때만 채운다. 마지막 작업이 최근인 것부터.</param>
public sealed record DashboardResponse(
    string? Project,
    IReadOnlyList<SessionRow> Recent,
    DashboardStats Stats,
    IReadOnlyList<Attention> Attention,
    IReadOnlyList<ProjectCard> Projects);

/// <summary>
/// 요약 탭 (docs/DECISIONS.md "요약은 고른 프로젝트의 첫 화면"). 옛 <c>DashboardViewModel</c> 의 숫자·최근 세션을 옮기고,
/// 계정 카드는 위 줄 계정 단추(<c>/api/accounts</c>)로 보냈다. 열린 터미널은 Stage 6 에서 더한다.
/// </summary>
public sealed class DashboardEndpoints : ITabEndpoints
{
    /// <summary>최근 세션 수. 옛 화면과 같다.</summary>
    public const int RecentCount = 5;

    public string Id => "dashboard";

    public void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", GetAsync).WithName("GetDashboard");
    }

    /// <param name="services">인덱스.</param>
    /// <param name="names">세션 이름.</param>
    /// <param name="settings">정리 기준.</param>
    /// <param name="tools">도구.</param>
    /// <param name="profiles">저장한 계정(계정 상태를 같이 읽는다).</param>
    /// <param name="plugins">플러그인 실패.</param>
    /// <param name="index">인덱스 실패.</param>
    /// <param name="catalog">모든 프로젝트일 때 카드 목록.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="project">프로젝트 경로. 비우면 모든 프로젝트.</param>
    private static async Task<DashboardResponse> GetAsync(
        IServiceProvider services,
        SessionNames names,
        ISettingsStore settings,
        ToolRegistry tools,
        IAuthProfileStore profiles,
        ToolPluginCatalog plugins,
        IndexService index,
        ProjectCatalog catalog,
        CancellationToken ct,
        string? project = null)
    {
        var scope = string.IsNullOrEmpty(project) ? null : project;
        IReadOnlyList<SessionInfo> sessions = [];
        UsageTokens week = UsageTokens.Zero;
        var attention = new List<Attention>();

        try
        {
            var sessionIndex = services.GetRequiredService<ISessionIndex>();
            sessions = await sessionIndex.ListAsync(SessionFilter.All with { ProjectPath = scope }, ct).ConfigureAwait(false);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var usage = await sessionIndex.GetUsageAsync(today.AddDays(-6), today, null, scope, ct).ConfigureAwait(false);
            week = UsageTokens.From(usage.Days.Aggregate(TokenUsage.Zero, (sum, day) => sum.Add(day.Usage)));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException)
        {
            attention.Add(new Attention("index", "bad", Detail: ex.Message));
        }

        if (index.Status.Error is { } indexError && attention.Count == 0)
        {
            attention.Add(new Attention("index", "bad", Detail: indexError));
        }

        foreach (var account in await AccountsEndpoints.ListAsync(tools, profiles, ct).ConfigureAwait(false))
        {
            // 깔리지 않은 도구는 로그인 안 된 게 당연하다. 손볼 것에 넣지 않는다
            if (account.Installed && account.State != "loggedIn")
            {
                attention.Add(new Attention("account", account.State == "expiringSoon" ? "warn" : "bad", account.Tool, account.State, account.ExpiresAt));
            }
        }

        var rule = settings.Current;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-rule.CleanupOlderThanDays);
        var tidy = sessions
            .Where(session => !session.IsActive && (session.ModifiedAt < cutoff || session.SizeBytes > rule.CleanupLargerThanMegabytes * 1024L * 1024L))
            .ToList();
        if (tidy.Count > 0)
        {
            attention.Add(new Attention("cleanup", "warn", Count: tidy.Count, Bytes: tidy.Sum(session => session.SizeBytes)));
        }

        if (scope is null)
        {
            attention.AddRange(plugins.Loads
                .Where(load => load.Errors.Count > 0)
                .Select(load => new Attention("plugin", "bad", Name: load.Label, Detail: load.Errors[0])));
        }

        return new DashboardResponse(
            scope,
            SessionsEndpoints.Rows([.. sessions.OrderByDescending(session => session.ModifiedAt).Take(RecentCount)], names),
            new DashboardStats(sessions.Count, sessions.Sum(session => session.SizeBytes), week),
            attention,
            scope is null ? await CardsAsync(catalog, sessions, ct).ConfigureAwait(false) : []);
    }

    private static async Task<IReadOnlyList<ProjectCard>> CardsAsync(ProjectCatalog catalog, IReadOnlyList<SessionInfo> sessions, CancellationToken ct)
    {
        var projects = await catalog.ListAsync(ct).ConfigureAwait(false);
        var labels = PathLabels.For([.. projects.Select(project => project.Path)], SessionLabels.Unknown);
        var size = sessions
            .Where(session => session.ProjectPath is not null)
            .GroupBy(session => session.ProjectPath!.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(session => session.SizeBytes), StringComparer.OrdinalIgnoreCase);

        return [.. projects.Select((project, i) => new ProjectCard(
            project.Path,
            labels[i],
            project.Exists,
            project.SessionCount,
            size.GetValueOrDefault(project.Path),
            project.LastActivity))];
    }
}

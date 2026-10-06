using Daiso.Core;
using Daiso.Host.Services;
using Daiso.Host.Shared;
using Daiso.Host.Tabs.Sessions;
using Daiso.Host.Tabs.Usage;

namespace Daiso.Host.Tabs.Dashboard;

/// <summary>"오늘 세션" 타일.</summary>
/// <param name="Sessions">오늘(이 PC 의 날짜) 바뀐 세션 수.</param>
/// <param name="Projects">그 세션들의 프로젝트 수(워크트리·임시 폴더는 원래 프로젝트로 센다).</param>
/// <param name="Yesterday">어제 바뀐 세션 수.</param>
public sealed record DashboardToday(int Sessions, int Projects, int Yesterday);

/// <summary>"이번 주 토큰" 막대 하나.</summary>
/// <param name="Date">날짜. 도구 기록의 날짜(UTC)다.</param>
/// <param name="Tokens">그날 토큰 합.</param>
public sealed record DashboardDay(DateOnly Date, long Tokens);

/// <summary>요약 화면. 열린 터미널은 <c>/api/terminal/rooms</c>, 한도는 <c>/api/limits</c>, 워크트리는 <c>/api/dashboard/worktrees</c> 에서 따로 받는다.</summary>
/// <param name="Project">지금 프로젝트 경로. null 이면 모든 프로젝트.</param>
/// <param name="Today">오늘 세션.</param>
/// <param name="Week">최근 7일, 오래된 날부터.</param>
/// <param name="Recent">최근 세션 최대 5개. 마지막 활동 순.</param>
public sealed record DashboardResponse(string? Project, DashboardToday Today, IReadOnlyList<DashboardDay> Week, IReadOnlyList<RecentSession> Recent);

/// <summary>
/// 요약 탭 (docs/DECISIONS.md "요약은 지금 상황: 타일 넷 · 최근 세션 5 · 워크트리", 시안 docs/design/summary.html).
/// 프로젝트를 고르면 같은 틀을 그 프로젝트(묶인 워크트리·임시 폴더 포함)로 좁힌다. 손볼 것은 위 줄 종(<c>/api/bell</c>)으로 옮겼다.
/// </summary>
public sealed class DashboardEndpoints : ITabEndpoints
{
    /// <summary>최근 세션 수.</summary>
    public const int RecentCount = 5;

    public string Id => "dashboard";

    public void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", GetAsync).WithName("GetDashboard");
        group.MapGet("/worktrees", GetWorktreesAsync).WithName("GetWorktrees");
    }

    /// <param name="services">인덱스.</param>
    /// <param name="names">세션 이름.</param>
    /// <param name="catalog">프로젝트로 좁히고, 오늘 프로젝트 수를 셀 때 묶음을 안다.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="project">프로젝트 경로. 비우면 모든 프로젝트.</param>
    private static async Task<DashboardResponse> GetAsync(
        IServiceProvider services,
        SessionNames names,
        ProjectCatalog catalog,
        CancellationToken ct,
        string? project = null)
    {
        var scope = string.IsNullOrEmpty(project) ? null : project;
        var members = await catalog.ScopeAsync(scope, ct).ConfigureAwait(false);
        var index = services.GetRequiredService<ISessionIndex>();

        var sessions = (await index.ListAsync(SessionFilter.All, ct).ConfigureAwait(false))
            .Where(session => ProjectCatalog.InScope(members, session.ProjectPath))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var owner = new Dictionary<string, ProjectItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in await catalog.ListAsync(ct).ConfigureAwait(false))
        {
            foreach (var member in item.Members)
            {
                owner.TryAdd(member, item);
            }
        }

        ProjectItem? OwnerOf(SessionInfo session) =>
            session.ProjectPath is { } path && owner.TryGetValue(path.TrimEnd('\\', '/'), out var item) ? item : null;

        static DateOnly Day(SessionInfo session) => DateOnly.FromDateTime(session.ModifiedAt.LocalDateTime);
        var todays = sessions.Where(session => Day(session) == today).ToList();
        var projects = todays
            .Select(session => OwnerOf(session)?.Path ?? session.ProjectPath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var usage = await UsageReport.ReadAsync(index, utcToday.AddDays(-6), utcToday, null, members, ct).ConfigureAwait(false);
        var byDay = usage.Days.ToDictionary(day => day.Date, day => day.Usage.Total);
        var week = Enumerable.Range(0, 7)
            .Select(i => utcToday.AddDays(i - 6))
            .Select(date => new DashboardDay(date, byDay.GetValueOrDefault(date)))
            .ToList();

        var latest = sessions.OrderByDescending(session => session.ModifiedAt).Take(RecentCount).ToList();
        var rows = SessionsEndpoints.Rows(latest, names);
        var recent = new List<RecentSession>();
        for (var i = 0; i < latest.Count; i++)
        {
            // 워크트리·임시 폴더 세션도 원래 프로젝트 이름으로 보인다(시안 docs/design/summary.html)
            var row = OwnerOf(latest[i]) is { } group ? rows[i] with { ProjectLabel = group.Name } : rows[i];
            recent.Add(await BellEndpoints.WithLastPromptAsync(index, row, ct).ConfigureAwait(false));
        }

        return new DashboardResponse(
            scope,
            new DashboardToday(todays.Count, projects, sessions.Count(session => Day(session) == today.AddDays(-1))),
            week,
            recent);
    }

    /// <param name="reader">git 을 읽는다.</param>
    /// <param name="catalog">프로젝트 폴더들.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="project">프로젝트 경로. 비우면 모든 프로젝트.</param>
    private static async Task<WorktreesResponse> GetWorktreesAsync(
        WorktreeReader reader,
        ProjectCatalog catalog,
        CancellationToken ct,
        string? project = null)
    {
        IEnumerable<string> folders = string.IsNullOrEmpty(project)
            ? (await catalog.ListAsync(ct).ConfigureAwait(false)).Where(item => item.Exists).SelectMany(item => item.Members)
            : await catalog.ScopeAsync(project, ct).ConfigureAwait(false) ?? (IEnumerable<string>)[project];

        return await reader.ReadAsync(folders, ct).ConfigureAwait(false);
    }
}

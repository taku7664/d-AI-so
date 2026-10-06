using Daiso.Core;
using Daiso.Core.Sessions;
using Daiso.Host.Services;
using Daiso.Host.Tabs.Sessions;
using Daiso.Providers.Manifest;

namespace Daiso.Host.Shared;

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

/// <summary>세션 한 줄과 그 세션에서 사람이 마지막으로 친 질문.</summary>
/// <param name="Session">세션.</param>
/// <param name="LastPrompt">마지막으로 친 질문(한 줄로 다듬음). 사람이 친 줄이 없으면 null.</param>
/// <param name="LastPromptAt">그 질문을 친 때.</param>
public sealed record RecentSession(SessionRow Session, string? LastPrompt, DateTimeOffset? LastPromptAt);

/// <summary>한 프로젝트에서 가장 최근에 하던 것.</summary>
/// <param name="ProjectPath">프로젝트(묶음) 경로.</param>
/// <param name="ProjectLabel">프로젝트 이름.</param>
/// <param name="Work">그 프로젝트의 가장 최근 세션.</param>
public sealed record ProjectWork(string ProjectPath, string ProjectLabel, RecentSession Work);

/// <summary>위 줄 종 팝업. 열린 터미널 방은 <c>/api/terminal/rooms</c> 에서 따로 받는다.</summary>
/// <param name="Attention">손볼 것. 팝업 맨 위.</param>
/// <param name="Last">마지막으로 하던 것. 세션이 없으면 null.</param>
/// <param name="Others">다른 프로젝트에서 하던 것. 프로젝트마다 가장 최근 것 하나, 최대 <see cref="BellEndpoints.OthersCount"/>개.</param>
public sealed record BellResponse(IReadOnlyList<Attention> Attention, ProjectWork? Last, IReadOnlyList<ProjectWork> Others);

/// <summary>
/// 위 줄 종 팝업 (docs/DECISIONS.md "요약은 지금 상황"). 어느 프로젝트를 골랐든 모든 프로젝트 기준이다.
/// </summary>
public static class BellEndpoints
{
    /// <summary>알림의 <c>tab</c> 자리에 넣는 이름. 인덱스·계정·세션이 바뀌면 다시 받는다.</summary>
    public const string Topic = "bell";

    public const int OthersCount = 4;

    /// <summary>마지막 질문을 찾을 때 거슬러 보는 사람 줄 수. 알림·요약 줄이 끼어 있어도 이 안에서 대개 찾는다.</summary>
    private const int PromptLookback = 40;

    public static void MapBell(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/bell", GetAsync).WithTags(Topic).WithName("GetBell");
    }

    /// <param name="services">인덱스.</param>
    /// <param name="names">세션 이름.</param>
    /// <param name="settings">정리 기준.</param>
    /// <param name="tools">도구.</param>
    /// <param name="profiles">저장한 계정(계정 상태를 같이 읽는다).</param>
    /// <param name="plugins">플러그인 실패.</param>
    /// <param name="index">인덱스 실패.</param>
    /// <param name="catalog">세션을 프로젝트 묶음으로 나눈다.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    private static async Task<BellResponse> GetAsync(
        IServiceProvider services,
        SessionNames names,
        ISettingsStore settings,
        ToolRegistry tools,
        IAuthProfileStore profiles,
        ToolPluginCatalog plugins,
        IndexService index,
        ProjectCatalog catalog,
        CancellationToken ct)
    {
        var attention = new List<Attention>();
        IReadOnlyList<SessionInfo> sessions = [];
        ISessionIndex? sessionIndex = null;

        try
        {
            sessionIndex = services.GetRequiredService<ISessionIndex>();
            sessions = await sessionIndex.ListAsync(SessionFilter.All, ct).ConfigureAwait(false);
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

        attention.AddRange(plugins.Loads
            .Where(load => load.Errors.Count > 0)
            .Select(load => new Attention("plugin", "bad", Name: load.Label, Detail: load.Errors[0])));

        if (sessionIndex is null || sessions.Count == 0)
        {
            return new BellResponse(attention, null, []);
        }

        // 세션을 프로젝트 묶음(워크트리·임시 폴더를 원래 프로젝트로)으로 나눠 묶음마다 가장 최근 것 하나
        var owner = new Dictionary<string, ProjectItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in await catalog.ListAsync(ct).ConfigureAwait(false))
        {
            foreach (var member in project.Members)
            {
                owner.TryAdd(member, project);
            }
        }

        var latest = sessions
            .Where(session => session.ProjectPath is not null && owner.ContainsKey(TrimEnd(session.ProjectPath)))
            .GroupBy(session => owner[TrimEnd(session.ProjectPath!)].Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(session => session.ModifiedAt)!)
            .OrderByDescending(session => session.ModifiedAt)
            .Take(OthersCount + 1)
            .ToList();

        var rows = SessionsEndpoints.Rows(latest, names);
        var works = new List<ProjectWork>();
        for (var i = 0; i < latest.Count; i++)
        {
            var project = owner[TrimEnd(latest[i].ProjectPath!)];
            works.Add(new ProjectWork(project.Path, project.Name, await WithLastPromptAsync(sessionIndex, rows[i], ct).ConfigureAwait(false)));
        }

        return new BellResponse(attention, works.FirstOrDefault(), [.. works.Skip(1)]);
    }

    /// <summary>세션 줄에 마지막으로 친 질문을 붙인다. 사람이 치지 않은 줄(<see cref="PromptNoise"/>)은 건너뛴다.</summary>
    internal static async Task<RecentSession> WithLastPromptAsync(ISessionIndex index, SessionRow row, CancellationToken ct)
    {
        var messages = await index.GetLatestUserMessagesAsync(row.Path, PromptLookback, ct).ConfigureAwait(false);
        var last = messages.FirstOrDefault(message => !PromptNoise.IsNoise(message.Text));
        return new RecentSession(row, last is null ? null : SessionTitle.Clean(last.Text), last?.At);
    }

    private static string TrimEnd(string path) =>
        path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
}

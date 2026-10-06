using Daiso.Core;
using Daiso.Core.Sessions;
using Daiso.Host.Notifications;
using Daiso.Host.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Daiso.Host.Tabs.Sessions;

/// <summary>
/// 세션 탭 (docs/ROADMAP.md Stage 5). 옛 <c>SessionsViewModel</c>(old/src/Daiso.App/ViewModels)의 도메인 로직을 옮겼다.
/// 세션은 파일 경로로 가리킨다. 경로를 받는 요청은 <b>인덱스에 있는 세션일 때만</b> 그 파일을 만진다 —
/// 화면이 보낸 아무 경로나 읽거나 지우지 않는다.
/// </summary>
public sealed class SessionsEndpoints : ITabEndpoints
{
    /// <summary>검색어 최소 길이. 옛 화면과 같다.</summary>
    public const int SearchMinimumLength = 2;

    /// <summary>세션 하나에서 보여 줄 검색 결과 수. 옛 화면과 같다.</summary>
    public const int MatchesPerSession = 20;

    /// <summary>대화 보기에서 읽을 최대 메시지 수와 보여 줄 수. 옛 화면과 같다.</summary>
    public const int RawMessageLimit = 20_000;

    public const int MessageLimit = 2_000;

    public string Id => "sessions";

    public void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).WithName("ListSessions");
        group.MapGet("/search", SearchAsync).WithName("SearchSessions");
        group.MapGet("/messages", MessagesAsync).WithName("GetSessionMessages");
        group.MapGet("/export", ExportAsync).WithName("ExportSession");
        group.MapPut("/name", RenameAsync).WithName("RenameSession");
        group.MapPost("/delete", DeleteAsync).WithName("DeleteSessions");
        group.MapPost("/resume", ResumeAsync).WithName("ResumeSession");
        group.MapPut("/cleanup", SetCleanupAsync).WithName("SetCleanupRule");
    }

    /// <param name="services">인덱스를 그때그때 꺼낸다.</param>
    /// <param name="names">붙인 이름.</param>
    /// <param name="settings">정리 기준.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="tool">도구 id. 비우면 모든 도구.</param>
    /// <param name="project">프로젝트 경로. 비우면 모든 프로젝트.</param>
    /// <param name="days">최근 며칠 안에 <b>시작한</b> 세션만. 0 이면 전체.</param>
    /// <param name="minMegabytes">이 크기 이상만. 0 이면 전체.</param>
    /// <param name="orphans">프로젝트 폴더가 없는 것만.</param>
    /// <param name="archived">보관함(Codex) 세션도 넣을지. 기본 넣는다.</param>
    private static async Task<Results<Ok<SessionsResponse>, ProblemHttpResult>> ListAsync(
        IServiceProvider services,
        SessionNames names,
        ISettingsStore settings,
        CancellationToken ct,
        string? tool = null,
        string? project = null,
        int days = 0,
        int minMegabytes = 0,
        bool orphans = false,
        bool archived = true)
    {
        ToolKind? kind = null;
        if (!string.IsNullOrEmpty(tool))
        {
            if (!ToolKind.TryParse(tool, out var parsed))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "도구 id 가 틀렸다");
            }

            kind = parsed;
        }

        var filter = new SessionFilter(
            kind,
            string.IsNullOrEmpty(project) ? null : project,
            days > 0 ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days) : null,
            null,
            orphans,
            minMegabytes > 0 ? minMegabytes * 1024L * 1024L : null,
            archived);

        var sessions = await services.GetRequiredService<ISessionIndex>().ListAsync(filter, ct).ConfigureAwait(false);
        var current = settings.Current;

        return TypedResults.Ok(new SessionsResponse(
            Rows(sessions, names),
            new CleanupRule(current.CleanupOlderThanDays, current.CleanupLargerThanMegabytes)));
    }

    /// <param name="services">인덱스.</param>
    /// <param name="names">붙인 이름.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="q">찾을 말. 두 글자 이상.</param>
    private static async Task<Results<Ok<SearchResponse>, ProblemHttpResult>> SearchAsync(
        IServiceProvider services,
        SessionNames names,
        CancellationToken ct,
        string? q = null)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length < SearchMinimumLength)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"검색어는 {SearchMinimumLength}글자 이상이어야 한다");
        }

        var hits = await services.GetRequiredService<ISessionIndex>().SearchAsync(query, ct).ConfigureAwait(false);
        var bySession = hits.GroupBy(hit => hit.Session.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = Rows([.. bySession.Select(group => group.First().Session)], names);

        var groups = bySession
            .Select((group, i) => new SearchGroup(
                rows[i],
                [.. group.Take(MatchesPerSession).Select(hit => new SearchMatch(Role(hit.Message.Role), hit.Message.At, hit.Snippet))]))
            .ToList();

        return TypedResults.Ok(new SearchResponse(groups, hits.Count));
    }

    /// <param name="services">인덱스와 도구.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="path">세션 파일 경로.</param>
    /// <param name="tools">도구 호출·시스템 메시지도 넣을지.</param>
    private static async Task<Results<Ok<MessagesResponse>, NotFound, ProblemHttpResult>> MessagesAsync(
        IServiceProvider services,
        CancellationToken ct,
        string path,
        bool tools = false)
    {
        if (await FindAsync(services, path, ct).ConfigureAwait(false) is not { } session)
        {
            return TypedResults.NotFound();
        }

        if (Provider(services, session) is not { } provider)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "이 세션의 도구를 모른다. 플러그인을 뺐을 수 있다");
        }

        var messages = new List<MessageRow>();
        var read = 0;
        var truncated = false;

        try
        {
            await foreach (var message in provider.ReadMessagesAsync(session.FilePath, 0, ct).ConfigureAwait(false))
            {
                if (++read > RawMessageLimit)
                {
                    truncated = true;
                    break;
                }

                if (!tools && message.Role is MessageRole.Tool or MessageRole.System)
                {
                    continue;
                }

                if (messages.Count == MessageLimit)
                {
                    truncated = true;
                    break;
                }

                // <command-…> 로 시작하는 줄(슬래시 명령 흔적)은 태그를 걷어 보여 준다. 옛 화면과 같다
                var text = message.Text.StartsWith("<command-", StringComparison.Ordinal) ? SessionTitle.Clean(message.Text) : message.Text;
                messages.Add(new MessageRow(Role(message.Role), message.At, text));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "세션 파일을 읽지 못했다", detail: ex.Message);
        }

        return TypedResults.Ok(new MessagesResponse(messages, truncated));
    }

    /// <summary>Markdown 으로 내려준다. 파일 이름은 <c>{도구}-{세션 id}.md</c>.</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> ExportAsync(
        IServiceProvider services,
        ISessionExporter exporter,
        CancellationToken ct,
        string path)
    {
        if (await FindAsync(services, path, ct).ConfigureAwait(false) is not { } session)
        {
            return TypedResults.NotFound();
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"daiso-export-{Guid.NewGuid():N}.md");
        try
        {
            await exporter.ExportMarkdownAsync(session, temporary, ExportOptions.Default, ct).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(temporary, ct).ConfigureAwait(false);
            return TypedResults.File(bytes, "text/markdown; charset=utf-8", $"{session.Tool.Id}-{session.Id}.md");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RenameAsync(
        RenameRequest request,
        IServiceProvider services,
        SessionNames names,
        NotificationHub hub,
        CancellationToken ct)
    {
        if (await FindAsync(services, request.Path, ct).ConfigureAwait(false) is not { } session)
        {
            return TypedResults.NotFound();
        }

        if (request.Name is { Length: > 200 })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "이름은 200자까지다");
        }

        names.Set(session.FilePath, request.Name);
        await hub.PublishAsync(new Notification("sessions", "changed"), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// 지운다. 실행 중 세션과 없는 파일은 건너뛰고 까닭을 돌려준다(<c>RecycleBinFileDisposer</c>). 끝나면 인덱스를 갱신한다.
    /// </summary>
    private static async Task<Ok<DeleteResponse>> DeleteAsync(
        DeleteRequest request,
        IServiceProvider services,
        IFileDisposer disposer,
        IndexService index,
        SessionNames names,
        CancellationToken ct)
    {
        var all = await services.GetRequiredService<ISessionIndex>().ListAsync(SessionFilter.All, ct).ConfigureAwait(false);
        var wanted = new HashSet<string>(request.Paths, StringComparer.OrdinalIgnoreCase);
        var targets = all.Where(session => wanted.Contains(session.FilePath)).ToList();
        var unknown = request.Paths
            .Where(path => !targets.Any(session => string.Equals(session.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new SkippedSession(path, "목록에 없는 세션이다"));

        var result = request.Permanent
            ? await disposer.DeletePermanentlyAsync(targets).ConfigureAwait(false)
            : await disposer.MoveToRecycleBinAsync(targets).ConfigureAwait(false);

        foreach (var path in result.Deleted)
        {
            names.Set(path, null);
        }

        index.TryStart(rebuild: false);

        return TypedResults.Ok(new DeleteResponse(
            result.Deleted,
            [.. result.Skipped.Select(skip => new SkippedSession(skip.Path, skip.Reason)), .. unknown]));
    }

    /// <summary>
    /// 이어서 연다. 지금은 바깥 터미널 창으로 연다. 앱 안 터미널(Stage 6)이 생기면 그쪽으로 바꾼다.
    /// 프로젝트 폴더가 없으면 사용자 폴더에서 연다. 연 폴더는 최근 폴더에 남긴다.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ResumeAsync(
        SessionRequest request,
        IServiceProvider services,
        ITerminalLauncher launcher,
        ISettingsStore settings,
        CancellationToken ct)
    {
        if (await FindAsync(services, request.Path, ct).ConfigureAwait(false) is not { } session)
        {
            return TypedResults.NotFound();
        }

        if (Provider(services, session) is not { } provider)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "이 세션의 도구를 모른다. 플러그인을 뺐을 수 있다");
        }

        var folder = session.ProjectPath is { } project && Directory.Exists(project)
            ? project
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        await launcher.LaunchAsync(folder, provider.LaunchTarget, provider.BuildResumeArguments(session)).ConfigureAwait(false);

        AppSettings.Remember(settings.Current.RecentFolders, folder);
        settings.Save();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CleanupRule>, ProblemHttpResult>> SetCleanupAsync(
        CleanupRule request,
        ISettingsStore settings,
        NotificationHub hub,
        CancellationToken ct)
    {
        if (request.OlderThanDays < 1 || request.LargerThanMegabytes < 1)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "기준은 1 이상이어야 한다");
        }

        settings.Current.CleanupOlderThanDays = request.OlderThanDays;
        settings.Current.CleanupLargerThanMegabytes = request.LargerThanMegabytes;
        settings.Save();

        await hub.PublishAsync(new Notification("sessions", "changed"), ct).ConfigureAwait(false);
        await hub.PublishAsync(new Notification("dashboard", "changed"), ct).ConfigureAwait(false);
        return TypedResults.Ok(request);
    }

    // ── 도움 ──

    internal static List<SessionRow> Rows(IReadOnlyList<SessionInfo> sessions, SessionNames names)
    {
        var labels = ProjectLabels(sessions);

        return [.. sessions.Select(session =>
        {
            var name = names.Get(session.FilePath);
            var orphan = session.ProjectPath is not { } project || !Directory.Exists(project);

            return new SessionRow(
                session.FilePath,
                session.Tool.Id,
                session.Id,
                name ?? SessionTitle.Clean(session.FirstPrompt),
                name is not null,
                session.ProjectPath,
                session.ProjectPath is { } key && labels.TryGetValue(key, out var label) ? label : SessionLabels.Unknown,
                session.StartedAt,
                session.ModifiedAt,
                session.SizeBytes,
                session.UserMessageCount,
                session.AssistantMessageCount,
                session.IsActive,
                session.IsArchived,
                orphan);
        })];
    }

    /// <summary>프로젝트 이름. 폴더 이름이 겹치면 상위 폴더까지 붙인다(옛 화면 <c>PathLabels</c> 와 같다).</summary>
    private static Dictionary<string, string> ProjectLabels(IReadOnlyList<SessionInfo> sessions)
    {
        var paths = sessions
            .Select(session => session.ProjectPath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var labels = PathLabels.For(paths, SessionLabels.Unknown);

        return paths.Select((path, i) => (path, labels[i])).ToDictionary(pair => pair.path, pair => pair.Item2, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<SessionInfo?> FindAsync(IServiceProvider services, string path, CancellationToken ct)
    {
        var all = await services.GetRequiredService<ISessionIndex>().ListAsync(SessionFilter.All, ct).ConfigureAwait(false);
        return all.FirstOrDefault(session => string.Equals(session.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static IProvider? Provider(IServiceProvider services, SessionInfo session) =>
        services.GetServices<IProvider>().FirstOrDefault(provider => provider.Kind == session.Tool);

    private static string Role(MessageRole role) => role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.Tool => "tool",
        _ => "system",
    };
}

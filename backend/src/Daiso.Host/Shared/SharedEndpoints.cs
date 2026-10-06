using Daiso.Host.Notifications;
using Daiso.Host.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Daiso.Host.Shared;

/// <summary>
/// 탭에 속하지 않는 공용 경로 (docs/ARCHITECTURE.md "탭에 속하지 않는 공용 경로").
/// 위 줄(프로젝트 선택기·종·계정)처럼 어느 탭에 있든 보이는 데이터만 둔다.
/// </summary>
public static class SharedEndpoints
{
    /// <summary>알림의 <c>tab</c> 자리에 넣는 이름.</summary>
    public const string ProjectsTopic = "projects";

    public static void MapShared(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var projects = app.MapGroup("/api/projects").WithTags(ProjectsTopic);
        projects.MapGet("/", ListProjectsAsync).WithName("ListProjects");
        projects.MapPut("/current", SetCurrentProjectAsync).WithName("SetCurrentProject");

        app.MapGet("/api/tools", ListTools).WithTags("tools").WithName("ListTools");

        var limits = app.MapGroup("/api/limits").WithTags(LimitsService.Topic);
        limits.MapGet("/", (LimitsService service) => service.Read()).WithName("GetLimits");
        limits.MapPut("/claude/statusline", SetStatusLineAsync).WithName("SetClaudeStatusLine");

        var index = app.MapGroup("/api/index").WithTags(IndexService.Topic);
        index.MapGet("/", (IndexService service) => service.Status).WithName("GetIndexStatus");
        index.MapPost("/refresh", (IndexService service) => Start(service, rebuild: false)).WithName("RefreshIndex");
        index.MapPost("/rebuild", (IndexService service) => Start(service, rebuild: true)).WithName("RebuildIndex");
    }

    /// <summary>도구 목록. 표시 순서대로. 플러그인으로 더한 도구도 들어 있다.</summary>
    private static IReadOnlyList<ToolItem> ListTools(ToolRegistry registry) =>
        [.. registry.Tools.Select(tool => new ToolItem(
            tool.Kind.Id,
            tool.Display.Title,
            tool.Display.Initial,
            tool.Display.Order,
            tool.Display.ColorStops))];

    /// <summary>
    /// Claude 상태줄 등록을 켜고 끈다. 사용자 설정 파일을 고치므로 화면에서 동의를 받은 뒤에만 부른다.
    /// 설정 파일이 JSON 객체가 아니면 손대지 않고 409 를 낸다.
    /// </summary>
    private static async Task<Results<Ok<LimitsResponse>, ProblemHttpResult>> SetStatusLineAsync(
        SetStatusLineRequest request,
        ClaudeStatusLine statusLine,
        LimitsService limits,
        NotificationHub hub,
        CancellationToken ct)
    {
        try
        {
            if (request.Enabled)
            {
                statusLine.Enable();
            }
            else
            {
                statusLine.Disable();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Claude 설정 파일을 고치지 못했다", detail: ex.Message);
        }

        await hub.PublishAsync(new Notification(LimitsService.Topic, "changed"), ct).ConfigureAwait(false);
        return TypedResults.Ok(limits.Read());
    }

    /// <summary>이미 돌고 있으면 새로 시작하지 않고 지금 상태를 돌려준다.</summary>
    private static Accepted<IndexStatus> Start(IndexService service, bool rebuild)
    {
        service.TryStart(rebuild);
        return TypedResults.Accepted("/api/index", service.Status);
    }

    private static async Task<ProjectsResponse> ListProjectsAsync(ProjectCatalog catalog, ISettingsStore settings, CancellationToken ct) =>
        new(await catalog.ListAsync(ct).ConfigureAwait(false), settings.Current.CurrentProject);

    private static async Task<Results<Ok<ProjectsResponse>, ProblemHttpResult>> SetCurrentProjectAsync(
        SetCurrentProjectRequest request,
        ProjectCatalog catalog,
        ISettingsStore settings,
        NotificationHub hub,
        CancellationToken ct)
    {
        var list = await catalog.ListAsync(ct).ConfigureAwait(false);
        string? path = null;

        if (request.Path is not null)
        {
            // 목록에 있는 것만 받는다. 화면이 보낸 아무 경로나 설정에 남기지 않는다
            path = list.FirstOrDefault(item => string.Equals(item.Path, request.Path, StringComparison.OrdinalIgnoreCase))?.Path;
            if (path is null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "목록에 없는 프로젝트다");
            }
        }

        settings.Current.CurrentProject = path;
        settings.Save();

        if (settings.LastSaveError is { } error)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "설정을 저장하지 못했다", detail: error);
        }

        await hub.PublishAsync(new Notification(ProjectsTopic, "changed"), ct).ConfigureAwait(false);

        return TypedResults.Ok(new ProjectsResponse(list, path));
    }
}

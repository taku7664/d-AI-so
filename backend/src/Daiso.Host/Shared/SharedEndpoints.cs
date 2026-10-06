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

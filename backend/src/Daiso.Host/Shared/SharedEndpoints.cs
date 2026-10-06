using Daiso.Host.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;
using Daiso.Host.Services;

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

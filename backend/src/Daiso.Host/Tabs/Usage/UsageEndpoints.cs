using Daiso.Core;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Daiso.Host.Tabs.Usage;

/// <summary>사용량 탭 (docs/ROADMAP.md Stage 4). 토큰만 센다. 구독 한도는 공용 경로 <c>/api/limits</c> 에 있다.</summary>
public sealed class UsageEndpoints : ITabEndpoints
{
    public string Id => "usage";

    public void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", GetAsync).WithName("GetUsage");
    }

    /// <param name="services">인덱스를 그때그때 꺼낸다. DB 가 깨졌으면 여기서 던지고 500 이 된다.</param>
    /// <param name="catalog">프로젝트 묶음(워크트리 포함).</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="grain"><c>day</c>(기본)·<c>week</c>·<c>month</c>.</param>
    /// <param name="tool">도구 id(<c>/api/tools</c>). 비우면 모든 도구.</param>
    /// <param name="project">프로젝트 경로(<c>/api/projects</c>). 비우면 모든 프로젝트.</param>
    private static async Task<Results<Ok<UsageResponse>, ProblemHttpResult>> GetAsync(
        IServiceProvider services,
        Shared.ProjectCatalog catalog,
        CancellationToken ct,
        string? grain = null,
        string? tool = null,
        string? project = null)
    {
        if (!TryGrain(grain, out var unit))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "grain 은 day·week·month 중 하나다");
        }

        ToolKind? kind = null;
        if (!string.IsNullOrEmpty(tool))
        {
            if (!ToolKind.TryParse(tool, out var parsed))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "도구 id 가 틀렸다");
            }

            kind = parsed;
        }

        // 날짜는 UTC 로 적혀 있다(세션 기록이 UTC). 옛 화면과 같게 오늘도 UTC 로 센다
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = await UsageReport.ReadAsync(
            services.GetRequiredService<ISessionIndex>(),
            new DateOnly(2000, 1, 1),
            today,
            kind,
            await catalog.ScopeAsync(project, ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        return TypedResults.Ok(UsageReport.Build(summary, unit, today));
    }

    private static bool TryGrain(string? value, out UsageGrain grain)
    {
        grain = UsageGrain.Day;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out grain) && Enum.IsDefined(grain);
    }
}

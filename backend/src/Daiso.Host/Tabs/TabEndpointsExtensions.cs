using System.Text.RegularExpressions;

namespace Daiso.Host.Tabs;

/// <summary>DI 에 등록된 탭을 <c>/api/{Id}</c> 에 단다.</summary>
public static partial class TabEndpointsExtensions
{
    /// <summary>
    /// 탭을 하나씩 단다. id 가 규칙에 안 맞거나 겹치면 뜨기 전에 던진다 —
    /// 둘이 같은 경로를 잡으면 어느 쪽이 답할지 요청이 와 봐야 안다.
    /// </summary>
    public static void MapTabs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tab in app.Services.GetServices<ITabEndpoints>())
        {
            if (!TabId().IsMatch(tab.Id))
            {
                throw new InvalidOperationException($"탭 id 는 소문자와 - 만 쓴다: {tab.Id}");
            }

            // health 처럼 탭이 아닌 경로와도 겹치면 안 된다
            if (string.Equals(tab.Id, "health", StringComparison.Ordinal) || !seen.Add(tab.Id))
            {
                throw new InvalidOperationException($"탭 id 가 겹친다: {tab.Id}");
            }

            tab.Map(app.MapGroup($"/api/{tab.Id}").WithTags(tab.Id));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex TabId();
}

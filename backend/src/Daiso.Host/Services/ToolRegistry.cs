using Daiso.Core;

namespace Daiso.Host.Services;

/// <summary>
/// 서버가 아는 도구를 한곳에서 센다 (old/docs/PLUGIN_PLAN.md Stage 2).
/// 옛 앱의 같은 이름 클래스에서 옮겨 왔다. 옛 것은 도구 목록을 화면 창구(<c>ToolLook</c>)에도 심었는데,
/// 표시는 이제 웹이 하므로 그 부분은 버렸다.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IEnumerable<IProvider> _providers;

    public ToolRegistry(IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>표시 순서대로 정렬한 도구 목록. 탭·카드가 이 순서로 돈다.</summary>
    public IReadOnlyList<IProvider> Tools =>
        [.. _providers
            .OrderBy(provider => provider.Display.Order)
            .ThenBy(provider => provider.Kind.Id, StringComparer.Ordinal)];
}

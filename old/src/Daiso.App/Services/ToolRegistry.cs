using Daiso.Core;

namespace Daiso.App.Services;

/// <summary>
/// 앱이 아는 도구를 한곳에서 센다. DI 가 물어 온 <see cref="IProvider"/> 를 들고 있다가
/// 화면이 쓰는 창구(<see cref="ToolLook"/>)에 심는다 (docs/PLUGIN_PLAN.md Stage 2).
/// <para>
/// 도구 목록이 <b>앱이 도는 중에 달라질 수 있어서</b> 따로 둔다 — 플러그인을 다시 읽으면 목록이 바뀐다.
/// 그때 <see cref="Refresh"/> 하나만 부르면 탭·카드·색이 같이 따라온다.
/// </para>
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

    /// <summary>지금 목록을 화면 창구에 심는다. 앱이 뜰 때 한 번, 플러그인을 다시 읽을 때 또 한 번.</summary>
    public void Refresh() => ToolLook.Register(_providers);
}

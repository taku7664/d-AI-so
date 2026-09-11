namespace Daiso.Core;

/// <summary>
/// 도구 id 로 제공자를 찾는 한 가지 방법.
///
/// <para>
/// <b>못 찾는 일이 정상이다.</b> 인덱스에는 지금 앱이 모르는 도구의 세션이 남아 있을 수 있다 —
/// 플러그인을 지웠거나(docs/PLUGIN_PLAN.md), 옛 기록의 id 를 읽지 못했거나.
/// 읽는 쪽은 그것을 견디도록 만들어 두었는데(<c>SqliteSessionIndex.ReadSession</c>),
/// 쓰는 쪽 아홉 군데가 <c>First(…)</c> 로 예외를 던지고 있었다 (2026-09-11 점검).
/// </para>
/// <para>
/// 그래서 찾기는 <b>언제나 null 을 돌려줄 수 있다</b>. 부르는 쪽은 없을 때 무엇을 보여 줄지 정한다.
/// </para>
/// </summary>
public static class ProviderLookup
{
    /// <summary>그 도구를 다루는 제공자. 앱이 그 도구를 모르면 null.</summary>
    public static IProvider? For(this IEnumerable<IProvider> providers, ToolKind kind)
    {
        ArgumentNullException.ThrowIfNull(providers);

        return providers.FirstOrDefault(provider => provider.Kind == kind);
    }
}

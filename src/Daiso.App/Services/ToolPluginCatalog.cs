using Daiso.Core;
using Daiso.Providers.Manifest;

namespace Daiso.App.Services;

/// <summary>
/// 읽어 낸 플러그인 목록과 <b>읽지 못한 것들</b> (docs/PLUGIN_PLAN.md Stage 4 · Stage 6).
/// <para>
/// 실패를 버리지 않고 들고 있는 것이 요점이다. 매니페스트를 놓았는데 도구가 안 보이면
/// 사람은 무엇이 틀렸는지 알 길이 없다 — 설정 화면이 이 목록을 그대로 보여 준다.
/// </para>
/// </summary>
public sealed class ToolPluginCatalog
{
    private readonly ToolPluginLoader? _loader;

    private IReadOnlyList<ToolPluginLoad> _loads;

    public ToolPluginCatalog(ToolPluginLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        _loader = loader;
        _loads = loader.Load();
    }

    private ToolPluginCatalog(string error)
    {
        _loader = null;
        _loads = [new ToolPluginLoad(string.Empty, null, [error])];
    }

    /// <summary>폴더를 아예 읽지 못했을 때. 앱은 내장 도구로 그냥 뜬다.</summary>
    public static ToolPluginCatalog Empty(string error) => new(error);

    /// <summary>읽기에 성공한 것과 실패한 것 전부. 설정 화면이 이 순서로 보여 준다.</summary>
    public IReadOnlyList<ToolPluginLoad> Loads => _loads;

    /// <summary>앱이 쓸 수 있는 플러그인 도구.</summary>
    public IReadOnlyList<IProvider> Tools => [.. _loads.Where(load => load.Ok).Select(load => load.Provider!)];

    /// <summary>읽지 못한 것.</summary>
    public IReadOnlyList<ToolPluginLoad> Failures => [.. _loads.Where(load => !load.Ok)];

    public bool HasFailures => _loads.Any(load => !load.Ok);

    /// <summary>플러그인을 놓은 폴더. 설정 화면이 "여기를 봅니다"로 보여 준다.</summary>
    public string Directory => ToolPluginLoader.DefaultDirectory;

    /// <summary>
    /// 어댑터마다 <c>hello</c> 를 한 번 주고받는다 (docs/PLUGIN_PLAN.md Stage 5).
    ///
    /// <para>
    /// <b>이걸 부르는 데가 없었다.</b> 판 협상도, 어댑터가 알려 주는 이름·이어읽기 여부도
    /// 테스트에서만 돌고 앱에서는 죽은 코드였다 (2026-09-11 점검). 앱이 뜬 뒤 배경에서 한 번 돈다.
    /// </para>
    /// <para>
    /// 말이 안 통하면 목록의 그 줄을 <b>실패로 바꾼다</b> — 설정 화면이 이유를 그대로 보여 준다.
    /// 이미 실린 도구를 여기서 빼지는 않는다. 그건 앱을 다시 켜야 하는 일이고, 화면이 그렇게 말한다.
    /// </para>
    /// </summary>
    public async Task VerifyAsync(CancellationToken ct)
    {
        var verified = new List<ToolPluginLoad>(_loads.Count);

        foreach (var load in _loads)
        {
            verified.Add(load.Provider is { } provider && !await Talks(provider, ct).ConfigureAwait(false)
                ? load with { Errors = [provider.LastAdapterError ?? "어댑터와 말이 통하지 않는다"] }
                : load);
        }

        _loads = verified;
    }

    private static async Task<bool> Talks(ManifestProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.HandshakeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 폴더를 다시 읽는다. <b>이미 실린 도구는 바뀌지 않는다</b> — 도구는 DI 가 앱이 뜰 때 물어 둔 것이라
    /// 새 도구가 화면에 나오려면 앱을 다시 켜야 한다. 그 사실을 설정 화면이 말한다.
    /// </summary>
    public void Reload()
    {
        if (_loader is not null)
        {
            _loads = _loader.Load();
        }
    }
}

using Daiso.App.Strings;
using Daiso.Providers.Manifest;

namespace Daiso.App.ViewModels;

/// <summary>
/// 설정 화면의 `도구 플러그인` 한 줄 (docs/PLUGIN_PLAN.md Stage 6).
/// <para>
/// 성공한 것과 <b>실패한 것</b>을 같은 모양으로 보여 준다. 매니페스트를 놓았는데 도구가 안 보이면
/// 사람은 무엇이 틀렸는지 알 길이 없다 — 그 자리에서 이유를 말한다.
/// </para>
/// </summary>
public sealed class ToolPluginRowViewModel
{
    private readonly ToolPluginLoad _load;

    public ToolPluginRowViewModel(ToolPluginLoad load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <summary>도구 이름. 읽지도 못했으면 파일 이름이 이름 노릇을 한다.</summary>
    public string Name => _load.Label;

    /// <summary>어느 파일에서 읽었나. 고치러 갈 곳이다.</summary>
    public string FilePath => _load.FilePath;

    public bool Ok => _load.Ok;

    public bool Failed => !_load.Ok;

    /// <summary>왜 안 됐는지. 여러 줄이면 줄바꿈으로 잇는다.</summary>
    public string ErrorText => string.Join(Environment.NewLine, _load.Errors);

    /// <summary>
    /// 상태 한 줄. 세션 기록을 읽을 수 있는지까지 말한다 —
    /// 어댑터 없이도 도구는 쓸 수 있지만 지난 대화는 안 보인다(docs/PLUGIN_PLAN.md §5 D).
    /// </summary>
    public string StateText
    {
        get
        {
            if (_load.Provider is not { } tool)
            {
                return UiStrings.Get("Plugins_Failed");
            }

            if (tool.AdapterError is { Length: > 0 } error)
            {
                return UiStrings.Format("Plugins_AdapterBroken", error);
            }

            return UiStrings.Get(tool.HasAdapter ? "Plugins_WithSessions" : "Plugins_WithoutSessions");
        }
    }

    /// <summary>
    /// 이 플러그인이 돌리는 바깥 명령. <b>그대로 보여 준다</b> — 사람이 놓은 파일이지만
    /// 무엇을 실행하는지 앱이 감추지 않는다 (docs/PLUGIN_PLAN.md §10).
    /// </summary>
    public string CommandText => _load.Provider?.Manifest.AdapterCommand ?? string.Empty;

    public bool HasCommand => CommandText.Length > 0;
}

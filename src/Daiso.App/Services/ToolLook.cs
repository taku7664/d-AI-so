using Daiso.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Daiso.App.Services;

/// <summary>
/// 도구를 화면에 그리는 창구. 이름·한 글자·색·로고를 여기서 묻는다. (ARCHITECTURE §6.2)
/// <para>
/// <b>값을 들고 있지 않는다.</b> 예전에는 도구별 <c>switch</c> 스무 곳이 여기 있었고, 그래서는 빌드된 앱에
/// 도구를 더할 수 없었다. 지금은 <see cref="IProvider.Display"/> 가 정본이고 이 클래스는 그것을 찾아
/// WinUI 타입(<see cref="Color"/> · <see cref="Brush"/> · <see cref="PathIcon"/>)으로 바꿔 줄 뿐이다
/// (docs/PLUGIN_PLAN.md Stage 2).
/// </para>
/// <para>
/// 앱이 뜰 때 <see cref="Register"/> 를 한 번 부른다. 부르기 전이나 모르는 도구를 물으면
/// <see cref="ToolDisplay.Unknown"/> 이 답한다 — 예외 대신 id 가 그대로 보인다.
/// </para>
/// </summary>
public static class ToolLook
{
    private static IReadOnlyDictionary<ToolKind, ToolDisplay> _displays =
        new Dictionary<ToolKind, ToolDisplay>();

    private static IReadOnlyList<ToolKind> _order = [];

    /// <summary>
    /// 앱이 아는 도구를 등록한다. 앱이 뜰 때 한 번(<c>App.OnLaunched</c>), 플러그인을 다시 읽을 때 또 한 번.
    /// <para>
    /// 정적 상태인 이유: 도구 표시를 묻는 곳이 XAML 템플릿 · 값 변환 · 뷰모델에 흩어져 있어
    /// 전부에 목록을 들려 보내려면 배선이 화면 코드를 다 훑는다. 앱 수명 동안 한 벌인 설정이라 <see cref="UiStrings"/> 와 같은 꼴로 둔다.
    /// </para>
    /// </summary>
    public static void Register(IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = providers.ToList();

        _displays = list.ToDictionary(provider => provider.Kind, provider => provider.Display);
        _order = [.. list
            .OrderBy(provider => provider.Display.Order)
            .ThenBy(provider => provider.Kind.Id, StringComparer.Ordinal)
            .Select(provider => provider.Kind)];
    }

    /// <summary>화면에 도구를 늘어놓는 순서. 탭·카드·필터가 모두 이 순서를 따른다.</summary>
    public static IReadOnlyList<ToolKind> DisplayOrder => _order;

    /// <summary>표시 순서상 위치. 모르는 도구는 맨 뒤.</summary>
    public static int Rank(ToolKind kind)
    {
        for (var i = 0; i < _order.Count; i++)
        {
            if (_order[i] == kind)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>표시 순서대로 정렬한다.</summary>
    public static IEnumerable<T> InDisplayOrder<T>(IEnumerable<T> items, Func<T, ToolKind> kindOf) =>
        items.OrderBy(item => Rank(kindOf(item)));

    /// <summary>그 도구의 표시 규칙. 모르는 도구면 id 를 그대로 보여 주는 기본값.</summary>
    public static ToolDisplay Of(ToolKind kind) =>
        _displays.TryGetValue(kind, out var display) ? display : ToolDisplay.Unknown(kind);

    /// <summary>카드 제목에 쓰는 정식 이름.</summary>
    public static string Title(ToolKind kind) => Of(kind).Title;

    /// <summary>만든 곳.</summary>
    public static string Vendor(ToolKind kind) => Of(kind).Vendor;

    /// <summary>버튼·미리보기에 쓰는 짧은 이름.</summary>
    public static string Short(ToolKind kind) => Of(kind).Short;

    /// <summary>동그란 배지 안 한 글자.</summary>
    public static string Initial(ToolKind kind) => Of(kind).Initial;

    /// <summary>배지 색. 그라데이션이면 첫 색.</summary>
    public static Color Color(ToolKind kind) => Parse(Of(kind).ColorStops.FirstOrDefault());

    /// <summary>
    /// 원형 배지의 채움. 색이 하나면 단색, 둘 이상이면 왼쪽 위 → 오른쪽 아래 그라데이션이다.
    /// 도구마다 어느 쪽인지 묻지 않는다 — 색 개수가 곧 답이다.
    /// </summary>
    public static Brush Brush(ToolKind kind)
    {
        var stops = Of(kind).ColorStops;

        if (stops.Count <= 1)
        {
            return new SolidColorBrush(Parse(stops.FirstOrDefault()));
        }

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
        };

        for (var i = 0; i < stops.Count; i++)
        {
            gradient.GradientStops.Add(new GradientStop
            {
                Color = Parse(stops[i]),
                Offset = stops.Count == 1 ? 0 : (double)i / (stops.Count - 1),
            });
        }

        return gradient;
    }

    /// <summary>제작사 로고의 SVG 경로(24×24). 없으면 채운 동그라미.</summary>
    public static string LogoPath(ToolKind kind) =>
        Of(kind).LogoPath is { Length: > 0 } path ? path : FallbackLogo;

    /// <summary>탭·메뉴에 쓰는 단색 로고 아이콘. 앞색을 따른다.</summary>
    public static PathIcon LogoIcon(ToolKind kind) =>
        new() { Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), LogoPath(kind)) };

    /// <summary>로고를 못 구한 도구. 글자가 아니라 도형이라 어느 나라 말에서도 같게 보인다.</summary>
    private const string FallbackLogo = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Z";

    /// <summary><c>#RRGGBB</c> 를 색으로. 꼴이 틀리면 회색이다 — 플러그인 매니페스트가 틀려도 화면은 떠야 한다.</summary>
    private static Color Parse(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Colors.Gray;
        }

        return Windows.UI.Color.FromArgb(255, r, g, b);
    }
}

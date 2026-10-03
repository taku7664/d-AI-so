namespace Daiso.Core;

/// <summary>
/// 도구를 화면에 어떻게 보일 것인가. <b>도구가 스스로 내놓는다</b> (docs/PLUGIN_PLAN.md Stage 2).
/// <para>
/// 예전에는 이름·제작사·색·로고가 <c>Daiso.App/Services/ToolLook.cs</c> 의 <c>switch (kind)</c> 스무 곳에 있었다.
/// 그래서는 빌드된 앱에 도구를 더할 수 없다 — 플러그인이 자기 이름과 색을 가질 방법이 없다.
/// </para>
/// <para>
/// <b>여기 값은 전부 문자열·숫자다.</b> Core 는 WinUI 를 모르고(<c>CorePurityTests</c>), 매니페스트에 그대로 적혀야 한다.
/// <c>Color</c>·<c>Brush</c>·<c>Geometry</c> 로 바꾸는 일은 <c>ToolLook</c> 이 한다.
/// </para>
/// </summary>
/// <param name="Title">카드 제목에 쓰는 정식 이름. 예: <c>Claude Code</c>.</param>
/// <param name="Vendor">
/// 만든 곳. AI CLI 를 처음 보는 사람에게 도구 이름은 글자일 뿐이라, 아는 회사 이름이 붙어야 감이 온다
/// (docs/TERMINAL_CARD_PLAN.md §4.3).
/// </param>
/// <param name="Short">버튼·미리보기에 쓰는 짧은 이름. 예: <c>Claude</c>.</param>
/// <param name="Initial">동그란 배지 안 한 글자.</param>
/// <param name="ColorStops">
/// 배지 색. <c>#RRGGBB</c> 꼴이다. <b>하나면 단색, 둘 이상이면 왼쪽 위에서 오른쪽 아래로 흐르는 그라데이션</b>이다.
/// 도구마다 단색/그라데이션을 따로 묻지 않고 이 하나로 정한다 — 매니페스트에 조건을 넣지 않으려는 것이다.
/// </param>
/// <param name="LogoPath">24×24 기준 SVG path. 비어 있으면 화면이 기본 동그라미를 쓴다.</param>
/// <param name="Order">화면에 늘어놓는 순서. 작은 것이 앞. 앱에 묻어 있는 도구가 0·1·2 를 쓰고 플러그인은 그 뒤다.</param>
public sealed record ToolDisplay(
    string Title,
    string Vendor,
    string Short,
    string Initial,
    IReadOnlyList<string> ColorStops,
    string LogoPath,
    int Order)
{
    /// <summary>플러그인이 순서를 안 정했을 때. 앱에 묻어 있는 도구 뒤에 붙는다.</summary>
    public const int DefaultOrder = 100;

    /// <summary>
    /// 아무것도 모르는 도구의 표시. 인덱스에 남아 있는데 플러그인이 사라진 경우에 쓴다 —
    /// 화면이 빈칸이나 예외 대신 id 를 그대로 보여 준다.
    /// </summary>
    public static ToolDisplay Unknown(ToolKind kind) => new(
        Title: kind.Id,
        Vendor: string.Empty,
        Short: kind.Id,
        Initial: kind.Id.Length > 0 ? kind.Id[..1].ToUpperInvariant() : "?",
        ColorStops: ["#808080"],
        LogoPath: string.Empty,
        Order: int.MaxValue);
}

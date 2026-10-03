using System.Globalization;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// ARCHITECTURE §6.2 · UI_REFACTOR_PLAN §6 — 페이지 골격은 문서가 아니라 이 테스트가 강제한다.
/// 규칙을 문서에만 적어 두고 요약·내 규칙에서 빠뜨린 일이 있어(2026-09-09) MUST 넷을 XAML 검사로 잠근다.
///
/// 1. 페이지는 대제목을 그리지 않는다 — <c>PageTitleText</c>가 페이지 XAML에 나오면 틀린 것이다 (셸이 그린다)
/// 2·7. 페이지 루트는 <c>PageBody</c>다 — 여백·줄 간격·슬롯 순서를 골격이 정한다
/// 3. 폭은 <c>Reading</c> / <c>Wide</c> 둘뿐이다
/// 4. 페이지가 선언한 칸 최소 폭의 합 + 좌측 메뉴 210 + 페이지 여백(PagePadding 좌우) ≤ 창 하한 1024
///    (칸 = <c>ColumnDefinition MinWidth</c>. 중첩 격자도 다 더한다 — 넉넉히 잡는 쪽이 안전하다. <c>DataTemplate</c> 안은 항목의 것이라 뺀다)
/// </summary>
public sealed class PageSkeletonTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ViewsDirectory = Path.Combine(Root, "src", "Daiso.App", "Views");

    private static readonly string AppXamlPath = Path.Combine(Root, "src", "Daiso.App", "App.xaml");

    /// <summary>창 하한. ShellWindow.MinimumWidth 와 같다 (UI_REFACTOR_PLAN §5.6).</summary>
    private const double WindowMinimumWidth = 1024;

    /// <summary>좌측 메뉴 폭. NavigationView OpenPaneLength 200 + 본문 격자 테두리 (UI_REFACTOR_PLAN §5.6).</summary>
    private const double NavigationPaneWidth = 210;

    public static IEnumerable<object[]> PageFiles() =>
        Directory.EnumerateFiles(ViewsDirectory, "*Page.xaml").Order(StringComparer.Ordinal).Select(path => new object[] { Path.GetFileName(path) });

    [Fact]
    public void There_are_seven_pages()
    {
        PageFiles().Should().HaveCount(7, because: "좌측 메뉴 일곱 항목 = 페이지 일곱. 늘면 이 수를 올리고 규칙도 같이 지킨다");
    }

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void Every_page_follows_the_skeleton(string fileName)
    {
        var document = XDocument.Load(Path.Combine(ViewsDirectory, fileName));

        var violations = PageSkeleton.Check(document, AppResources());

        violations.Should().BeEmpty(because: $"{fileName} 은 UI_REFACTOR_PLAN §6 의 MUST 를 지켜야 한다");
    }

    // ── 규칙을 어긴 페이지를 만들면 빨개지는지 (Stage 7 완료 기준) ─────────────

    [Fact]
    public void A_page_that_draws_its_own_title_is_rejected()
    {
        var violations = PageSkeleton.Check(Page("""
            <controls:PageBody Layout="Reading">
                <controls:PageBody.Body>
                    <TextBlock Text="요약" Style="{StaticResource PageTitleText}" />
                </controls:PageBody.Body>
            </controls:PageBody>
            """), AppResources());

        violations.Should().ContainSingle().Which.Should().Contain("PageTitleText");
    }

    [Fact]
    public void A_page_whose_root_is_not_PageBody_is_rejected()
    {
        var violations = PageSkeleton.Check(Page("""
            <Grid Padding="24">
                <TextBlock Text="본문" />
            </Grid>
            """), AppResources());

        violations.Should().ContainSingle().Which.Should().Contain("PageBody");
    }

    [Fact]
    public void A_page_with_a_third_width_is_rejected()
    {
        var violations = PageSkeleton.Check(Page("""
            <controls:PageBody Layout="Compact">
                <controls:PageBody.Body><Grid /></controls:PageBody.Body>
            </controls:PageBody>
            """), AppResources());

        violations.Should().ContainSingle().Which.Should().Contain("Layout");
    }

    [Fact]
    public void A_page_that_cannot_fit_the_window_minimum_is_rejected()
    {
        var violations = PageSkeleton.Check(Page("""
            <controls:PageBody Layout="Wide">
                <controls:PageBody.Body>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="{StaticResource ListPaneWidth}" MinWidth="{StaticResource ListPaneMinWidth}" />
                            <ColumnDefinition Width="*" MinWidth="600" />
                        </Grid.ColumnDefinitions>
                    </Grid>
                </controls:PageBody.Body>
            </controls:PageBody>
            """), AppResources());

        violations.Should().ContainSingle().Which.Should().Contain("1024");
    }

    [Fact]
    public void Page_resources_before_the_root_are_allowed()
    {
        var violations = PageSkeleton.Check(Page("""
            <Page.Resources>
                <x:Double x:Key="Anything">1</x:Double>
            </Page.Resources>
            <controls:PageBody Layout="Wide">
                <controls:PageBody.Body><Grid /></controls:PageBody.Body>
            </controls:PageBody>
            """), AppResources());

        violations.Should().BeEmpty();
    }

    // ── 도우미 ───────────────────────────────────────────────────────────────

    private static XDocument Page(string body) => XDocument.Parse($"""
        <Page
            x:Class="Daiso.App.Views.FakePage"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:controls="using:Daiso.App.Controls">
        {body}
        </Page>
        """);

    private static PageSkeleton.Resources AppResources() => PageSkeleton.Resources.Load(XDocument.Load(AppXamlPath));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Daiso.sln 이 있는 저장소 루트를 찾지 못했다");
    }

    /// <summary>골격 검사 본체. 파일과 무관하게 XAML 문서를 받아 위반 목록을 돌려준다 — 그래서 어긴 예를 만들어 빨개지는지 볼 수 있다.</summary>
    private static class PageSkeleton
    {
        private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace Controls = "using:Daiso.App.Controls";

        private static readonly HashSet<string> Layouts = new(StringComparer.Ordinal) { "Reading", "Wide" };

        public static IReadOnlyList<string> Check(XDocument page, Resources resources)
        {
            var violations = new List<string>();
            var root = page.Root ?? throw new InvalidOperationException("빈 XAML");

            // 1. 대제목은 셸이 그린다
            var titles = root.Descendants()
                .Where(e => (string?)e.Attribute("Style") == "{StaticResource PageTitleText}")
                .ToList();
            if (titles.Count > 0)
            {
                violations.Add($"페이지가 PageTitleText 를 {titles.Count}곳에서 직접 쓴다. 대제목은 셸의 NavigationView.Header 가 그린다 (IPageHeaderSource)");
            }

            // 2·7. 루트는 PageBody
            var content = root.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.', StringComparison.Ordinal));
            if (content is null || content.Name != Controls + "PageBody")
            {
                violations.Add($"페이지 루트가 {content?.Name.LocalName ?? "(없음)"} 이다. 루트는 controls:PageBody 여야 한다 (여백·줄 간격·슬롯 순서는 골격이 정한다)");
                return violations;
            }

            // 3. 폭은 두 값
            var layout = (string?)content.Attribute("Layout");
            if (layout is null || !Layouts.Contains(layout))
            {
                violations.Add($"PageBody Layout=\"{layout ?? "(없음)"}\" — Reading 또는 Wide 여야 한다. 세 번째 폭은 없다 (UI_REFACTOR_PLAN §8.2)");
            }

            // 4. 하한 1024 에 들어간다. DataTemplate 안의 열은 목록 항목·팝업의 것이라 페이지 폭과 무관하므로 빼고, 나머지는 중첩이어도 다 더한다
            var columns = content.Descendants(Presentation + "ColumnDefinition")
                .Where(e => !e.Ancestors(Presentation + "DataTemplate").Any())
                .Select(e => (string?)e.Attribute("MinWidth"))
                .Where(value => !string.IsNullOrEmpty(value))
                .Select(value => resources.ResolveDouble(value!))
                .ToList();
            var required = columns.Sum() + NavigationPaneWidth + resources.PagePaddingHorizontal;
            if (required > WindowMinimumWidth)
            {
                violations.Add(
                    $"칸 최소 폭의 합 {columns.Sum():0} + 좌측 메뉴 {NavigationPaneWidth:0} + 여백 {resources.PagePaddingHorizontal:0} = {required:0} > 창 하한 {WindowMinimumWidth:0}. " +
                    "좁을 때 접거나 MinWidth 를 낮춘다 (UI_REFACTOR_PLAN §5.6)");
            }

            return violations;
        }

        /// <summary>App.xaml 의 공용 값. MinWidth 가 {StaticResource …} 로 적혀 있으면 여기서 푼다.</summary>
        public sealed class Resources
        {
            private readonly Dictionary<string, double> _doubles;

            private Resources(Dictionary<string, double> doubles, double pagePaddingHorizontal)
            {
                _doubles = doubles;
                PagePaddingHorizontal = pagePaddingHorizontal;
            }

            public double PagePaddingHorizontal { get; }

            public static Resources Load(XDocument appXaml)
            {
                var root = appXaml.Root ?? throw new InvalidOperationException("빈 App.xaml");
                var doubles = root.Descendants(Xaml + "Double")
                    .Where(e => e.Attribute(Xaml + "Key") is not null)
                    .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => Parse(e.Value), StringComparer.Ordinal);

                var padding = root.Descendants(Presentation + "Thickness")
                    .FirstOrDefault(e => (string?)e.Attribute(Xaml + "Key") == "PagePadding")
                    ?? throw new InvalidOperationException("App.xaml 에 PagePadding 이 없다");

                return new Resources(doubles, HorizontalOf(padding.Value));
            }

            public double ResolveDouble(string value)
            {
                if (value.StartsWith("{StaticResource ", StringComparison.Ordinal))
                {
                    var key = value["{StaticResource ".Length..].TrimEnd('}').Trim();
                    return _doubles.TryGetValue(key, out var found)
                        ? found
                        : throw new InvalidOperationException($"App.xaml 에 x:Double {key} 가 없다");
                }

                return Parse(value);
            }

            /// <summary>Thickness "24" / "24,20" / "24,20,24,20" 의 좌 + 우.</summary>
            private static double HorizontalOf(string thickness)
            {
                var parts = thickness.Split(',', StringSplitOptions.TrimEntries).Select(Parse).ToArray();
                return parts.Length switch
                {
                    1 => parts[0] * 2,
                    2 => parts[0] * 2,
                    4 => parts[0] + parts[2],
                    _ => throw new InvalidOperationException($"Thickness 꼴이 아니다: {thickness}"),
                };
            }

            private static double Parse(string value) => double.Parse(value.Trim(), CultureInfo.InvariantCulture);
        }
    }
}

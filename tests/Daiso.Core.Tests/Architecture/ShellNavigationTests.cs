using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// UI_REFACTOR_PLAN §8.1 — 좌측 메뉴 항목 하나가 화면 하나다.
///
/// <para>
/// <c>ShellWindow.Navigate</c> 의 switch 는 <c>_ =&gt; typeof(DashboardPage)</c> 로 끝난다.
/// 메뉴 항목을 더하고 그 <c>Tag</c> 를 switch 에 넣는 걸 잊으면, 눌렀을 때
/// <b>오류 없이 요약 화면이 뜬다</b>. 무엇이 잘못됐는지 화면 어디에도 나오지 않는다.
/// 폴백 자체는 필요하다(switch 식은 모든 입력을 받아야 한다) — 그래서 빠진 것을 밖에서 본다.
/// </para>
/// </summary>
public sealed partial class ShellNavigationTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string AppSource = Path.Combine(Root, "src", "Daiso.App");

    private static readonly string ShellXamlPath = Path.Combine(AppSource, "ShellWindow.xaml");

    private static readonly string ShellCodePath = Path.Combine(AppSource, "ShellWindow.xaml.cs");

    /// <summary>폴백이 가리키는 화면. 이것만 switch 에 없어도 된다.</summary>
    private const string FallbackTag = "Dashboard";

    [Fact]
    public void Every_menu_item_has_a_page_wired_to_it()
    {
        var switchBody = NavigateSwitchBody();

        var unwired = MenuTags()
            .Where(tag => tag != FallbackTag)
            .Where(tag => !switchBody.Contains($"\"{tag}\"", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        unwired.Should().BeEmpty(
            because: "Navigate 의 switch 에 없는 Tag 는 폴백으로 흘러 조용히 요약 화면이 뜬다");
    }

    [Fact]
    public void Every_wired_page_type_exists()
    {
        var missing = WiredPageTypes()
            .Where(type => !File.Exists(Path.Combine(AppSource, "Views", type + ".xaml")))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(because: "Navigate 가 가리키는 화면 파일이 있어야 한다");
    }

    [Fact]
    public void Every_page_file_is_reachable_from_the_menu()
    {
        var wired = WiredPageTypes().ToHashSet(StringComparer.Ordinal);
        wired.Add("DashboardPage");

        var orphans = Directory.EnumerateFiles(Path.Combine(AppSource, "Views"), "*Page.xaml")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !wired.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            because: "메뉴에서 갈 수 없는 화면은 만들다 만 것이거나 지우다 만 것이다. PageSkeletonTests 는 개수만 세고 도달 가능성은 보지 않는다");
    }

    [Fact]
    public void The_menu_and_the_page_count_agree()
    {
        MenuTags().Should().HaveCount(7, because: "좌측 메뉴 일곱 항목 = 페이지 일곱 (PageSkeletonTests 와 같은 수)");
    }

    // ── 검사기 자체가 도는지 ──────────────────────────────────────────────

    [Fact]
    public void A_tag_that_is_not_in_the_switch_is_caught()
    {
        const string Body = "\"Usage\" => typeof(UsagePage), _ => typeof(DashboardPage),";

        new[] { "Usage", "Cleanup" }
            .Where(tag => !Body.Contains($"\"{tag}\"", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Be("Cleanup");
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    /// <summary>좌측 메뉴 항목의 Tag. XAML 을 글이 아니라 트리로 읽는다.</summary>
    private static IReadOnlyList<string> MenuTags()
    {
        var document = XDocument.Load(ShellXamlPath);

        return
        [
            .. document.Descendants()
                .Where(element => element.Name.LocalName == "NavigationViewItem")
                .Select(element => element.Attribute("Tag")?.Value)
                .OfType<string>(),
        ];
    }

    /// <summary><c>Navigate</c> 안 switch 식의 본문.</summary>
    private static string NavigateSwitchBody()
    {
        var source = File.ReadAllText(ShellCodePath);
        var at = source.IndexOf("private void Navigate(string tag)", StringComparison.Ordinal);

        at.Should().BeGreaterThan(0, because: "ShellWindow 에 Navigate(string tag) 가 있어야 한다. 이름을 바꿨으면 이 테스트도 같이 고친다");

        var start = source.IndexOf("switch", at, StringComparison.Ordinal);
        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return source[start..end];
    }

    /// <summary>switch 가 가리키는 화면 타입 이름들.</summary>
    private static IEnumerable<string> WiredPageTypes() =>
        PageTypePattern().Matches(NavigateSwitchBody()).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"typeof\((\w+Page)\)")]
    private static partial Regex PageTypePattern();

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
}

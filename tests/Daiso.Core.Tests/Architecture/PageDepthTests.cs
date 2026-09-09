namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// docs/REVIEW_BACKLOG.md R6 — 화면 트리가 더 깊어지지 않게 붙잡는 래칫.
///
/// <para>
/// 깊이는 그 자체로 버그가 아니지만, 한 번 15겹이 되면 <c>&lt;/Grid&gt;</c> 하나가 무엇을 닫는지
/// 읽어서 알 수 없게 된다. 고치기 어려운 화면은 그렇게 만들어진다.
/// </para>
///
/// <para>
/// <b>이 표는 목표가 아니라 지금 값이다.</b> 줄이는 것은 화면을 다시 짜는 일이고 여기서 하지 않는다.
/// 이 테스트가 막는 것은 <b>되돌아감</b>이다 — 다음에 화면을 손볼 때 한 겹 더 얹으면 빨개진다.
/// 줄였으면 이 표의 수도 같이 내린다(그래야 래칫이 조여진다).
/// </para>
/// </summary>
public sealed class PageDepthTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string AppSource = Path.Combine(Root, "src", "Daiso.App");

    /// <summary>
    /// 2026-09-10 측정값. 내리는 것은 환영, 올리는 것은 안 된다.
    /// <para>
    /// 목록 한 줄을 <c>Ui/ItemTemplates.xaml</c> 로 옮긴 화면은 그만큼 얕아졌다.
    /// 옮겨 간 사전도 여기 함께 적는다 — 안 적으면 "옮겨서 얕아진 것"이 눈속임이 된다.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, int> Budget = new(StringComparer.Ordinal)
    {
        ["ShellWindow.xaml"] = 6,
        ["PromptsPage.xaml"] = 10,
        ["UsagePage.xaml"] = 10,
        ["SessionsPage.xaml"] = 11,
        ["ItemTemplates.xaml"] = 7,
        ["SettingsPage.xaml"] = 12,
        ["RuleMakerPage.xaml"] = 14,
        ["TerminalPage.xaml"] = 14,
        ["DashboardPage.xaml"] = 16,
    };

    public static IEnumerable<object[]> Pages() => Budget.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_page_does_not_get_deeper(string fileName)
    {
        var depth = Depth(File.ReadAllText(Find(fileName)));

        depth.Should().BeLessThanOrEqualTo(
            Budget[fileName],
            because: $"{fileName} 의 트리가 예산({Budget[fileName]}겹)보다 깊어졌다. 겹을 더하는 대신 형제로 두거나 칸을 나눈다");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void The_budget_is_not_left_stale(string fileName)
    {
        var depth = Depth(File.ReadAllText(Find(fileName)));

        depth.Should().BeGreaterThanOrEqualTo(
            Budget[fileName] - 2,
            because: $"{fileName} 이 예산보다 {Budget[fileName] - depth}겹 얕아졌다. 표의 수를 내려 래칫을 조인다");
    }

    [Fact]
    public void Every_page_is_in_the_budget()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(AppSource, "Views"), "*Page.xaml")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal);

        pages.Should().OnlyContain(name => Budget.ContainsKey(name),
            because: "새 화면도 예산에 적어야 한다. 안 적으면 이 테스트가 그 화면을 아예 안 본다");
    }

    // ── 검사기 자체가 도는지 ──────────────────────────────────────────────

    [Fact]
    public void Depth_counts_nesting_not_attribute_elements()
    {
        // Grid > StackPanel > TextBlock = 3. Grid.ColumnDefinitions 같은 속성 요소는 겹이 아니다
        const string Markup = """
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
              </Grid.ColumnDefinitions>
              <StackPanel>
                <TextBlock />
              </StackPanel>
            </Grid>
            """;

        Depth(Markup).Should().Be(3);
    }

    [Fact]
    public void Depth_ignores_markup_inside_comments()
    {
        Depth("<Grid><!-- <a><b><c><d><e><f><g> --><TextBlock /></Grid>").Should().Be(2);
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 여는 태그의 최대 중첩. 자기 닫는 태그와 <c>Grid.ColumnDefinitions</c> 같은
    /// 속성 요소(이름에 점이 있다)는 겹으로 세지 않는다 — 화면을 읽는 사람이 겹으로 느끼지 않기 때문이다.
    /// </summary>
    internal static int Depth(string xaml)
    {
        var body = System.Text.RegularExpressions.Regex.Replace(xaml, "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var depth = 0;
        var deepest = 0;

        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(body, @"<(/?)([A-Za-z][\w.:]*)[^>]*?(/?)>",
                System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var closing = match.Groups[1].Value.Length > 0;
            var selfClosing = match.Groups[3].Value.Length > 0;

            if (match.Groups[2].Value.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (closing)
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (selfClosing)
            {
                // 자기 닫는 잎도 한 겹을 차지한다. 이걸 안 세면 <TextBlock /> 과
                // <TextBlock></TextBlock> 의 깊이가 달라져, 값이 트리가 아니라 쓰는 방식에 좌우된다
                deepest = Math.Max(deepest, depth + 1);
            }
            else
            {
                depth++;
                deepest = Math.Max(deepest, depth);
            }
        }

        return deepest;
    }

    private static string Find(string fileName)
    {
        foreach (var folder in new[] { "Views", "Ui", "." })
        {
            var candidate = Path.Combine(AppSource, folder, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"예산에 적힌 파일을 찾지 못했다: {fileName}");
    }

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

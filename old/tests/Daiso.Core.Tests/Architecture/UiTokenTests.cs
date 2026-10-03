using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// docs/UI_TOKENS_PLAN.md — 글자 크기 · 모서리 · 간격의 정본은 <c>App.xaml</c> 하나다.
/// 다른 XAML 은 숫자를 적지 않고 <c>{StaticResource}</c> 로 참조한다.
///
/// <para>
/// 이 테스트가 없으면 다음에 화면을 하나 더 붙일 때 다시 <c>FontSize="12"</c> 가 들어오고,
/// 268곳을 모아 둔 일이 조용히 되돌아간다. 문서가 아니라 여기가 규칙을 지킨다.
/// (같은 방식: <see cref="StringResourceKeysTests"/> · <see cref="PageSkeletonTests"/>)
/// </para>
/// </summary>
public sealed partial class UiTokenTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string AppSource = Path.Combine(Root, "src", "Daiso.App");

    private static readonly string AppXamlPath = Path.Combine(AppSource, "App.xaml");

    /// <summary>값의 정본. 여기만 날 숫자를 적을 수 있다.</summary>
    private const string TokenHome = "App.xaml";

    /// <summary>"간격 없음"은 디자인 값이 아니라 끄는 것이다. 토큰을 만들지 않는다.</summary>
    private static readonly string[] AllowedLiterals = ["0"];

    [Fact]
    public void No_page_writes_a_raw_size_gap_or_radius()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles().Where(path => Path.GetFileName(path) != TokenHome))
        {
            foreach (var (property, value, line) in RawValues(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetFileName(file)}:{line}  {property}=\"{value}\"");
            }
        }

        offenders.Should().BeEmpty(
            because: "이 값들의 정본은 App.xaml 이다. 숫자 대신 {StaticResource ...} 로 쓴다 (docs/UI_TOKENS_PLAN.md)");
    }

    [Fact]
    public void Every_token_a_page_refers_to_exists_in_app_xaml()
    {
        var defined = DefinedTokens();
        var missing = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in TokenReferencePattern().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[2].Value;

                if (!defined.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)}  {key}");
                }
            }
        }

        missing.Should().BeEmpty(because: "없는 자원을 참조하면 화면이 그려질 때 터진다. 빌드는 잡아 주지 않는다");
    }

    /// <summary>
    /// WinUI 가 스스로 읽어 가는 기본값 덮어쓰기. 우리 코드는 이름을 부르지 않으므로
    /// "아무도 안 쓴다"로 보이지만 지우면 제목줄 크기가 되돌아간다.
    /// </summary>
    private static readonly string[] FrameworkOverrides =
    [
        "TitleBarCompactHeight",
        "TitleBarExpandedHeight",
    ];

    [Fact]
    public void App_xaml_has_no_token_that_nobody_uses()
    {
        var used = new HashSet<string>(FrameworkOverrides, StringComparer.Ordinal);

        // XAML 의 {StaticResource}/{ThemeResource} 와 C# 의 Resources["..."] 둘 다 참조다.
        // XAML 만 보면 PageBody 가 코드에서 꺼내 쓰는 PageMaxWidth 를 죽은 것으로 잘못 잡는다.
        foreach (var file in XamlFiles())
        {
            foreach (Match match in AnyResourceReferencePattern().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        foreach (var file in SourceFiles())
        {
            foreach (Match match in CodeResourceLookupPattern().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        var dead = DefinedTokens()
            .Where(key => !used.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        dead.Should().BeEmpty(because: "쓰지 않는 토큰은 지운다. 값을 미리 만들어 두지 않는다 (UI_TOKENS_PLAN §2)");
    }

    // ── 규칙을 어기면 정말 빨개지는지 ────────────────────────────────────

    [Theory]
    [InlineData("<TextBlock FontSize=\"12\" />")]
    [InlineData("<StackPanel Spacing=\"8\" />")]
    [InlineData("<Grid ColumnSpacing=\"12\" />")]
    [InlineData("<Border CornerRadius=\"6\" />")]
    [InlineData("<Setter Property=\"FontSize\" Value=\"14\" />")]
    public void A_raw_value_is_caught(string markup)
    {
        RawValues(markup).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("<TextBlock FontSize=\"{StaticResource CaptionFontSize}\" />")]
    [InlineData("<StackPanel Spacing=\"0\" />")]
    [InlineData("<!-- 여백은 Spacing=\"8\" 로 두었다 -->")]
    [InlineData("<Setter Property=\"FontSize\" Value=\"{StaticResource BodyFontSize}\" />")]
    public void A_token_reference_a_zero_and_a_comment_are_fine(string markup)
    {
        RawValues(markup).Should().BeEmpty();
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    /// <summary>주석을 뺀 본문에서 날 숫자로 적힌 자리를 (속성, 값, 줄번호)로 돌려준다.</summary>
    private static IEnumerable<(string Property, string Value, int Line)> RawValues(string xaml)
    {
        var body = CommentPattern().Replace(xaml, match => new string('\n', match.Value.Count(c => c == '\n')));

        foreach (Match match in RawAttributePattern().Matches(body))
        {
            if (!AllowedLiterals.Contains(match.Groups[2].Value.Trim()))
            {
                yield return (match.Groups[1].Value, match.Groups[2].Value, Line(body, match.Index));
            }
        }

        foreach (Match match in RawSetterPattern().Matches(body))
        {
            if (!AllowedLiterals.Contains(match.Groups[2].Value.Trim()))
            {
                yield return (match.Groups[1].Value, match.Groups[2].Value, Line(body, match.Index));
            }
        }
    }

    private static int Line(string text, int index) =>
        text.Take(index).Count(c => c == '\n') + 1;

    private static HashSet<string> DefinedTokens()
    {
        var document = XDocument.Load(AppXamlPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document.Descendants()
            .Where(element => element.Name.LocalName is "Double" or "CornerRadius")
            .Select(element => element.Attribute(x + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(AppSource, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(AppSource, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    /// <summary>범위 안 속성. 긴 이름을 먼저 적어 ColumnSpacing 이 Spacing 으로 잘리지 않게 한다.</summary>
    private const string Scoped = "MinColumnSpacing|MinRowSpacing|ColumnSpacing|RowSpacing|CornerRadius|FontSize|Spacing";

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\b(" + Scoped + @")=""([^""{}]+)""")]
    private static partial Regex RawAttributePattern();

    [GeneratedRegex(@"<Setter\s+Property=""(" + Scoped + @")""\s+Value=""([^""{}]+)""")]
    private static partial Regex RawSetterPattern();

    [GeneratedRegex(@"\b(" + Scoped + @")=""\{StaticResource ([A-Za-z0-9]+)\}""")]
    private static partial Regex TokenReferencePattern();

    [GeneratedRegex(@"\{StaticResource ([A-Za-z0-9]+)\}")]
    private static partial Regex AnyResourceReferencePattern();

    /// <summary>코드에서 자원을 꺼내는 자리: <c>Resources["PageMaxWidth"]</c>.</summary>
    [GeneratedRegex(@"Resources\[""([A-Za-z0-9]+)""\]")]
    private static partial Regex CodeResourceLookupPattern();

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

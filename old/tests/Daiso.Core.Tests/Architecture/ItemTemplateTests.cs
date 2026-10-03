using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// docs/REVIEW_BACKLOG.md A3 — 목록 한 줄의 생김새는 <c>Ui/ItemTemplates.xaml</c> 한 곳에 모은다.
///
/// <para>
/// 화면이 <c>ItemTemplate="{StaticResource X}"</c> 로 부르는데 그 X 가 사전에 없으면,
/// <b>빌드는 통과하고 그 화면을 열 때 터진다.</b> 그래서 여기서 미리 맞춰 본다.
/// </para>
/// </summary>
public sealed partial class ItemTemplateTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string AppSource = Path.Combine(Root, "src", "Daiso.App");

    private static readonly string DictionaryPath = Path.Combine(AppSource, "Ui", "ItemTemplates.xaml");

    [Fact]
    public void Every_template_a_page_asks_for_is_in_the_dictionary()
    {
        var defined = DefinedKeys();
        var missing = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in TemplateReferencePattern().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[2].Value;

                if (key.EndsWith("Template", StringComparison.Ordinal) && !defined.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)}  {key}");
                }
            }
        }

        missing.Should().BeEmpty(
            because: "없는 템플릿을 부르면 그 화면을 열 때 터진다. 빌드는 잡아 주지 않는다");
    }

    [Fact]
    public void The_dictionary_has_no_template_that_nobody_asks_for()
    {
        var used = XamlFiles()
            .SelectMany(file => TemplateReferencePattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);

        DefinedKeys().Where(key => !used.Contains(key)).Order(StringComparer.Ordinal)
            .Should().BeEmpty(because: "아무도 안 쓰는 템플릿은 지운다");
    }

    [Fact]
    public void Every_template_says_what_data_it_draws()
    {
        XDocument.Load(DictionaryPath).Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate")
            .Should().OnlyContain(
                element => element.Attribute(XName.Get("DataType", "http://schemas.microsoft.com/winfx/2006/xaml")) != null,
                because: "x:DataType 이 없으면 {x:Bind} 가 컴파일되지 않고 느린 {Binding} 으로 밀린다");
    }

    [Fact]
    public void The_moved_templates_carry_no_page_code_behind()
    {
        var text = File.ReadAllText(DictionaryPath);

        HandlerPattern().Matches(text).Select(match => match.Value).Should().BeEmpty(
            because: "이 사전에는 자기완결적인 것만 둔다. 핸들러가 필요한 템플릿은 페이지에 남긴다");
    }

    private static HashSet<string> DefinedKeys()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument.Load(DictionaryPath).Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate")
            .Select(element => element.Attribute(x + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(AppSource, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [GeneratedRegex(@"(\w*Template)=""\{StaticResource (\w+)\}""")]
    private static partial Regex TemplateReferencePattern();

    [GeneratedRegex(@"\b(?:Click|Loaded|Unloaded|Tapped|PointerEntered|PointerExited|PointerMoved|SelectionChanged)=""\w+""")]
    private static partial Regex HandlerPattern();

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

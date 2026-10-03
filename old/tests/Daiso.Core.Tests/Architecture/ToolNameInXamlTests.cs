using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// <b>화면 XAML 에 도구 이름을 적지 않는다</b> (docs/PLUGIN_PLAN.md Stage 3).
/// <para>
/// 적어 두면 빌드된 앱에 도구를 더해도 그 자리에는 안 나온다 — 탭 셋이 그랬다.
/// 도구가 나오는 자리는 전부 <c>ToolLook.DisplayOrder</c> 나 뷰모델의 목록을 돈다.
/// </para>
/// <para>
/// 도구 이름이 <b>있어도 되는 곳</b>은 문구 파일(<c>Resources.resw</c>)과 코드다 —
/// 문구는 사람이 읽는 글이고, 코드에는 앱에 묻어 있는 도구 셋이 실제로 있다.
/// </para>
/// </summary>
public sealed partial class ToolNameInXamlTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ViewsDirectory = Path.Combine(Root, "src", "Daiso.App");

    /// <summary>앱에 묻어 있는 도구의 이름. 화면 XAML 에 글자로 나오면 안 된다.</summary>
    private static readonly string[] ToolNames = ["Claude", "Codex", "Antigravity", "Gemini"];

    public static TheoryData<string> XamlFiles()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory
            .EnumerateFiles(ViewsDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(ViewsDirectory, path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void No_page_spells_out_a_tool_name(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(ViewsDirectory, relativePath));

        // 주석은 뺀다. "예전에는 Codex 탭이 여기 있었다" 같은 설명까지 막을 이유가 없다
        var markup = CommentPattern().Replace(text, string.Empty);

        var found = ToolNames.Where(name => markup.Contains(name, StringComparison.Ordinal)).ToList();

        found.Should().BeEmpty(
            because: $"{relativePath} 에 도구 이름이 박히면 빌드된 앱에 도구를 더해도 그 자리에는 안 나온다. "
                + "도구가 나오는 자리는 ToolLook.DisplayOrder 나 뷰모델 목록을 돈다");
    }

    /// <summary>검사기 자체가 도는지. 주석이 아닌 자리에 이름이 있으면 걸려야 한다.</summary>
    [Fact]
    public void A_tool_name_outside_a_comment_is_caught()
    {
        const string Markup = "<!-- Codex 는 여기 없었다 --><TextBlock Text=\"Claude\" />";

        var stripped = CommentPattern().Replace(Markup, string.Empty);

        ToolNames.Where(name => stripped.Contains(name, StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Be("Claude");
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CommentPattern();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Daiso.sln 을 찾지 못했다");
    }
}

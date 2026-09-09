namespace Daiso.Core.Tests.Prompts;

/// <summary>
/// 기본 프롬프트 파일은 저장소에 두 벌 있다: 앱에 묻히는 <c>src/Daiso.Core/Resources/Prompts</c> 와
/// 사람이 읽는 <c>docs/prompts</c>. 한쪽만 고치면 앱이 파는 절차와 문서가 조용히 갈라진다.
/// 두 벌을 하나로 합치기 전까지는 이 테스트가 어긋남을 잡는다.
/// </summary>
public sealed class BuiltInPromptFilesTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string Embedded = Path.Combine(Root, "src", "Daiso.Core", "Resources", "Prompts");

    private static readonly string Docs = Path.Combine(Root, "docs", "prompts");

    [Fact]
    public void The_two_copies_hold_the_same_file_names()
    {
        Names(Embedded).Should().Equal(Names(Docs));
    }

    [Fact]
    public void The_two_copies_hold_the_same_text()
    {
        var different = Names(Embedded)
            .Where(name => File.Exists(Path.Combine(Docs, name)))
            .Where(name => Read(Path.Combine(Embedded, name)) != Read(Path.Combine(Docs, name)))
            .ToList();

        different.Should().BeEmpty(because: "docs/prompts 와 Resources/Prompts 는 같은 내용이어야 한다");
    }

    [Fact]
    public void Every_embedded_prompt_is_in_the_catalog_and_parses()
    {
        BuiltInPrompts.EmbeddedIds.Should().BeEquivalentTo(BuiltInPrompts.Ids);
        BuiltInPrompts.List().Should().HaveCount(BuiltInPrompts.Ids.Count);
    }

    /// <summary>줄바꿈 차이는 무시한다. 두 벌이 다른 도구를 거쳐 저장될 수 있다.</summary>
    private static string Read(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static IReadOnlyList<string> Names(string directory) =>
    [
        .. Directory.EnumerateFiles(directory, "*.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

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

namespace Daiso.Core.Tests.Prompts;

/// <summary>ARCHITECTURE §5.8 — 절차형 프롬프트 파일 형식과 기본 제공 프롬프트.</summary>
public sealed class PromptPresetTests
{
    private const string Sample = """
        ---
        name: 시험 프롬프트
        description: 한 줄 설명
        category: fixing
        output: docs/OUT.md
        ---

        # 제목

        본문이다.
        """;

    [Fact]
    public void Parse_reads_the_front_matter_and_the_body()
    {
        var preset = PromptPresetSerializer.Parse("sample", Sample);

        preset.Id.Should().Be("sample");
        preset.Name.Should().Be("시험 프롬프트");
        preset.Description.Should().Be("한 줄 설명");
        preset.Category.Should().Be(PromptCategory.Fixing);
        preset.Output.Should().Be("docs/OUT.md");
        preset.Body.Should().StartWith("# 제목").And.EndWith("본문이다.");
    }

    [Theory]
    [InlineData("planning", PromptCategory.Planning)]
    [InlineData("understanding", PromptCategory.Understanding)]
    [InlineData("fixing", PromptCategory.Fixing)]
    [InlineData("release", PromptCategory.Release)]
    [InlineData("other", PromptCategory.Other)]
    public void Parse_reads_every_category(string written, PromptCategory expected)
    {
        // `other` 는 2026-09-11 에 붙였다. 갈래 값은 파일에 글자로 적히므로 이름과 순서를 바꾸지 않는다
        var text = Sample.Replace("category: fixing", "category: " + written, StringComparison.Ordinal);

        PromptPresetSerializer.Parse("sample", text).Category.Should().Be(expected);
    }

    [Fact]
    public void Every_category_has_a_label_key()
    {
        // 갈래를 늘려 놓고 문구를 안 넣으면 콤보에 빈 줄이 생긴다. StringResourceKeysTests 는
        // 코드에 리터럴이 없어 이 조합을 못 본다 — 여기서 이름을 맞춰 둔다
        var defined = System.Xml.Linq.XDocument.Parse(File.ReadAllText(StringsPath)).Root!
            .Elements("data")
            .Select(data => (string?)data.Attribute("name"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var category in Enum.GetValues<PromptCategory>())
        {
            defined.Should().Contain("PromptCategory_" + category, because: "갈래마다 화면 문구가 있어야 한다");
        }
    }

    private static readonly string StringsPath = FindStringsPath();

    private static string FindStringsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Daiso.sln 을 찾지 못했다");

        return Path.Combine(root, "src", "Daiso.App", "Strings", "ko-KR", "Resources.resw");
    }

    [Fact]
    public void Serialize_then_parse_gives_the_same_content()
    {
        var once = PromptPresetSerializer.Parse("sample", Sample);
        var text = PromptPresetSerializer.Serialize(once);
        var twice = PromptPresetSerializer.Parse("sample", text);

        twice.Should().Be(once);
        PromptPresetSerializer.Serialize(twice).Should().Be(text);
    }

    [Theory]
    [InlineData("본문만 있고 앞머리가 없다")]
    [InlineData("---\nname: x\n")]
    [InlineData("---\nname: x\ncategory: planning\nweird: y\n---\n본문")]
    [InlineData("---\ncategory: planning\n---\n본문")]
    [InlineData("---\nname: x\ncategory: nope\n---\n본문")]
    [InlineData("---\nname: x\ncategory: planning\n---\n")]
    // Enum.TryParse 는 숫자 문자열도 받는다. 정의에 없는 값으로 새면 갤러리 묶음이 빈칸이 된다
    [InlineData("---\nname: x\ncategory: 99\n---\n본문")]
    [InlineData("---\nname: x\ncategory: -1\n---\n본문")]
    public void Broken_files_are_rejected_with_a_reason(string text)
    {
        var act = () => PromptPresetSerializer.Parse("x", text);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void The_starter_message_points_at_the_project_file_not_the_body()
    {
        var preset = PromptPresetSerializer.Parse("planning-interview", Sample);

        var message = PromptPresetSerializer.StarterMessage(preset);

        message.Should().Contain("docs/prompts/planning-interview.md");
        message.Should().Contain("docs/OUT.md");
        message.Should().NotContain("본문이다");
    }

    [Fact]
    public void Built_in_catalog_and_embedded_files_match()
    {
        BuiltInPrompts.Ids.Order(StringComparer.Ordinal).Should().Equal(BuiltInPrompts.EmbeddedIds);
    }

    [Fact]
    public void Every_built_in_prompt_parses_and_says_where_it_writes()
    {
        var prompts = BuiltInPrompts.List();

        prompts.Should().NotBeEmpty();

        foreach (var prompt in prompts)
        {
            prompt.Name.Should().NotBeNullOrWhiteSpace();
            prompt.Description.Should().NotBeNullOrWhiteSpace(because: $"{prompt.Id}는 갤러리에서 한 줄 설명이 보여야 한다");
            prompt.Output.Should().NotBeNullOrWhiteSpace(because: $"{prompt.Id}는 결과 파일이 어디인지 말해야 한다");
            prompt.Body.Should().Contain("파일을 만들", because: $"{prompt.Id}는 코딩 에이전트가 먼저 파일을 만들지 않게 막는 문장이 있어야 한다");
        }
    }

    [Fact]
    public void The_planning_interview_starts_with_exactly_five_numbered_questions()
    {
        var body = BuiltInPrompts.List().Single(prompt => prompt.Id == "planning-interview").Body;
        var start = body[body.LastIndexOf("## 인터뷰 시작", StringComparison.Ordinal)..];

        var numbered = start.Split('\n').Count(line => line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.');

        numbered.Should().Be(5);
    }

    [Fact]
    public void Built_in_prompts_use_a_single_h1()
    {
        foreach (var prompt in BuiltInPrompts.List())
        {
            prompt.Body.Split('\n').Count(line => line.StartsWith("# ", StringComparison.Ordinal))
                .Should().Be(1, because: $"{prompt.Id}의 H1은 제목 하나여야 한다");
        }
    }
}

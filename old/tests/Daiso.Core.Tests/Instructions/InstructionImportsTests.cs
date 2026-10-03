namespace Daiso.Core.Tests.Instructions;

/// <summary>REQUIREMENTS §7 — Claude 전용 `@경로` import 판별.</summary>
public sealed class InstructionImportsTests
{
    [Fact]
    public void A_whole_line_at_path_is_an_import()
    {
        InstructionImports.Find("# 규칙\n@docs/style.md\n").Should().Equal("docs/style.md");
    }

    [Fact]
    public void Leading_whitespace_is_allowed()
    {
        InstructionImports.Find("  @docs/style.md\n").Should().Equal("docs/style.md");
    }

    [Fact]
    public void A_mention_inside_a_sentence_is_not_an_import()
    {
        InstructionImports.Find("@docs/style.md 를 읽어라\n").Should().BeEmpty();
    }

    [Fact]
    public void Imports_inside_code_fences_are_ignored()
    {
        InstructionImports.Find("```\n@docs/style.md\n```\n").Should().BeEmpty();
    }

    [Fact]
    public void Duplicates_are_reported_once()
    {
        InstructionImports.Find("@a.md\n@a.md\n").Should().Equal("a.md");
    }

    [Fact]
    public void Order_follows_the_file()
    {
        InstructionImports.Find("@b.md\n@a.md\n").Should().Equal("b.md", "a.md");
    }

    [Fact]
    public void An_empty_file_has_no_imports()
    {
        InstructionImports.Find(null).Should().BeEmpty();
        InstructionImports.Find(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void A_lone_at_sign_is_not_an_import()
    {
        InstructionImports.Find("@\n").Should().BeEmpty();
    }
}

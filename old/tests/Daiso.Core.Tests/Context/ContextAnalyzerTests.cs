namespace Daiso.Core.Tests.Context;

/// <summary>ARCHITECTURE §5.4 — 인메모리 ContextFile로 중복·충돌을 검출한다.</summary>
public sealed class ContextAnalyzerTests
{
    private readonly ContextAnalyzer _analyzer = new();

    [Fact]
    public void Total_chars_counts_only_existing_files()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "12345"),
            Missing("b.md"),
        ]);

        report.TotalChars.Should().Be(5);
        report.Files.Should().HaveCount(2);
    }

    [Fact]
    public void A_line_repeated_across_two_files_is_a_duplicate()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "- 커밋 메시지는 한 줄로 쓴다\n첫 파일만의 줄이다"),
            File("b.md", "커밋 메시지는 한 줄로 쓴다"),
        ]);

        report.Duplicates.Should().ContainSingle()
            .Which.Files.Should().BeEquivalentTo(["a.md", "b.md"]);
    }

    [Fact]
    public void Bullets_and_case_and_spacing_are_normalized_before_comparing()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "* Always Answer  In English"),
            File("b.md", "always answer in english"),
        ]);

        report.Duplicates.Should().ContainSingle()
            .Which.NormalizedText.Should().Be("always answer in english");
    }

    [Fact]
    public void A_line_repeated_inside_one_file_is_not_a_duplicate_across_files()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "커밋 메시지는 한 줄로 쓴다\n커밋 메시지는 한 줄로 쓴다"),
        ]);

        report.Duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Short_lines_are_ignored()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "# 제목"),
            File("b.md", "# 제목"),
        ]);

        report.Duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Opposing_language_instructions_are_reported_as_a_conflict()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "모든 응답은 한국어로 작성한다"),
            File("b.md", "answer everything in english please"),
        ]);

        report.Conflicts.Should().NotBeEmpty();
        report.Conflicts[0].Reason.Should().Contain("언어");
    }

    [Fact]
    public void Always_and_never_are_reported_as_a_conflict()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "always run the unit tests first"),
            File("b.md", "never run the unit tests yourself"),
        ]);

        report.Conflicts.Should().NotBeEmpty();
    }

    [Fact]
    public void A_line_containing_both_keywords_is_not_a_conflict_on_its_own()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "you must not commit without running tests"),
        ]);

        report.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void No_conflicts_when_the_instructions_agree()
    {
        var report = _analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "모든 응답은 한국어로 작성한다"),
            File("b.md", "모든 응답은 한국어로 작성한다"),
        ]);

        report.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void The_tool_is_carried_into_the_report()
    {
        _analyzer.Analyze(ToolKind.Codex, []).Tool.Should().Be(ToolKind.Codex);
    }

    [Fact]
    public void Custom_keyword_pairs_are_honoured()
    {
        var analyzer = new ContextAnalyzer([new ConflictKeywordPair("탭 문자", "스페이스 문자", "들여쓰기 충돌")]);

        var report = analyzer.Analyze(ToolKind.Claude, [
            File("a.md", "들여쓰기는 탭 문자를 쓴다"),
            File("b.md", "들여쓰기는 스페이스 문자를 쓴다"),
        ]);

        report.Conflicts.Should().ContainSingle()
            .Which.Reason.Should().Be("들여쓰기 충돌");
    }

    [Fact]
    public void The_embedded_keyword_table_is_not_empty()
    {
        ConflictKeywords.Load().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("  - Hello   World  ", "hello world")]
    [InlineData("## 제목 ", "제목")]
    [InlineData("> 인용 줄", "인용 줄")]
    [InlineData("* * 별 두 개", "별 두 개")]
    public void Line_normalization_strips_bullets_and_collapses_spaces(string raw, string expected)
    {
        ContextAnalyzer.NormalizeLine(raw).Should().Be(expected);
    }

    private static ContextFile File(string path, string content) =>
        new(path, "claude", content, Exists: true, Order: 0);

    private static ContextFile Missing(string path) =>
        new(path, "claude", string.Empty, Exists: false, Order: 1);
}

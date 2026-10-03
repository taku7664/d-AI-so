namespace Daiso.Core.Tests.Instructions;

/// <summary>ARCHITECTURE §5.6 — 마이그레이션 diff·복사의 입력이 되는 본문 추출.</summary>
public sealed class InstructionMarkerStripTests
{
    private const string Body = "@PROJECT_RULES.daiso\nRules above are YAML.";

    private readonly InstructionMarkerWriter _writer = new();

    [Fact]
    public void Strip_removes_the_block_and_the_blank_line_it_left()
    {
        const string Existing = "# 프로젝트\n\n기존 지시문\n";

        _writer.Strip(_writer.Apply(Existing, Body)).Should().Be(Existing);
    }

    [Fact]
    public void Strip_keeps_crlf_files_as_crlf()
    {
        const string Existing = "# 프로젝트\r\n\r\n기존 지시문\r\n";

        _writer.Strip(_writer.Apply(Existing, Body)).Should().Be(Existing);
    }

    [Fact]
    public void Strip_keeps_content_that_follows_the_block()
    {
        const string Content = "머리\n\n<!-- daiso:start -->\n본문\n<!-- daiso:end -->\n\n꼬리\n";

        _writer.Strip(Content).Should().Be("머리\n\n꼬리\n");
    }

    [Fact]
    public void Strip_without_markers_returns_the_original()
    {
        const string Content = "마커가 없다\n";

        _writer.Strip(Content).Should().Be(Content);
    }

    [Fact]
    public void Strip_of_a_block_only_file_is_empty()
    {
        _writer.Strip(_writer.Apply(null, Body)).Should().BeEmpty();
    }

    [Fact]
    public void Strip_of_an_empty_file_is_empty()
    {
        _writer.Strip(string.Empty).Should().BeEmpty();
    }
}

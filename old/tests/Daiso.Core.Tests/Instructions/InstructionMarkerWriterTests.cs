using System.Text;

namespace Daiso.Core.Tests.Instructions;

/// <summary>ARCHITECTURE §7.3 — 블록 밖은 개행 문자까지 바이트 불변.</summary>
public sealed class InstructionMarkerWriterTests
{
    private const string Body = "@PROJECT_RULES.daiso\nRules above are YAML.";

    private readonly InstructionMarkerWriter _writer = new();

    [Fact]
    public void A_null_content_becomes_a_fresh_block()
    {
        _writer.Apply(null, Body).Should().Be(
            "<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML.\n<!-- daiso:end -->\n");
    }

    [Fact]
    public void An_empty_content_becomes_a_fresh_block()
    {
        _writer.Apply(string.Empty, Body).Should().Be(
            "<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML.\n<!-- daiso:end -->\n");
    }

    [Fact]
    public void Without_markers_the_block_is_appended_after_a_blank_line_lf()
    {
        const string Existing = "# 프로젝트\n\n기존 지시문\n";

        var result = _writer.Apply(Existing, Body);

        result.Should().Be(
            Existing
            + "\n"
            + "<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML.\n<!-- daiso:end -->\n");
        result.Should().StartWith(Existing);
    }

    [Fact]
    public void Without_markers_the_block_is_appended_after_a_blank_line_crlf()
    {
        const string Existing = "# 프로젝트\r\n\r\n기존 지시문\r\n";

        var result = _writer.Apply(Existing, Body);

        result.Should().Be(
            Existing
            + "\r\n"
            + "<!-- daiso:start -->\r\n@PROJECT_RULES.daiso\r\nRules above are YAML.\r\n<!-- daiso:end -->\r\n");
    }

    [Fact]
    public void Content_without_a_trailing_newline_gets_one_before_the_block()
    {
        const string Existing = "# 프로젝트";

        var result = _writer.Apply(Existing, Body);

        result.Should().Be(
            "# 프로젝트\n\n<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML.\n<!-- daiso:end -->\n");
    }

    [Fact]
    public void An_existing_block_is_replaced_in_place_lf()
    {
        const string Existing =
            "# 프로젝트\n\n<!-- daiso:start -->\n낡은 내용\n<!-- daiso:end -->\n\n뒤에 오는 문단\n";

        var result = _writer.Apply(Existing, Body);

        result.Should().Be(
            "# 프로젝트\n\n<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML.\n<!-- daiso:end -->\n\n뒤에 오는 문단\n");
    }

    [Fact]
    public void An_existing_block_is_replaced_in_place_crlf()
    {
        const string Existing =
            "# 프로젝트\r\n\r\n<!-- daiso:start -->\r\n낡은 내용\r\n<!-- daiso:end -->\r\n\r\n뒤에 오는 문단\r\n";

        var result = _writer.Apply(Existing, Body);

        result.Should().Be(
            "# 프로젝트\r\n\r\n<!-- daiso:start -->\r\n@PROJECT_RULES.daiso\r\nRules above are YAML.\r\n<!-- daiso:end -->\r\n\r\n뒤에 오는 문단\r\n");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Bytes_outside_the_block_are_untouched(string newline)
    {
        var head = $"# 프로젝트{newline}{newline}지켜야 하는 앞부분{newline}{newline}";
        var tail = $"{newline}{newline}지켜야 하는 뒷부분{newline}";
        var existing = $"{head}<!-- daiso:start -->{newline}낡은 내용{newline}<!-- daiso:end -->{tail}";

        var result = _writer.Apply(existing, Body);

        var start = result.IndexOf(InstructionMarkerWriter.StartMarker, StringComparison.Ordinal);
        var end = result.IndexOf(InstructionMarkerWriter.EndMarker, StringComparison.Ordinal);

        Encoding.UTF8.GetBytes(result[..start]).Should().Equal(Encoding.UTF8.GetBytes(head));
        Encoding.UTF8.GetBytes(result[(end + InstructionMarkerWriter.EndMarker.Length)..])
            .Should().Equal(Encoding.UTF8.GetBytes(tail));
    }

    [Fact]
    public void Applying_twice_is_idempotent()
    {
        const string Existing = "# 프로젝트\r\n\r\n본문\r\n";

        var once = _writer.Apply(Existing, Body);
        var twice = _writer.Apply(once, Body);

        twice.Should().Be(once);
    }

    [Fact]
    public void A_body_with_crlf_is_normalized_to_the_file_newline()
    {
        var result = _writer.Apply("# 제목\n", "첫 줄\r\n둘째 줄\r\n");

        result.Should().Be("# 제목\n\n<!-- daiso:start -->\n첫 줄\n둘째 줄\n<!-- daiso:end -->\n");
    }

    [Fact]
    public void A_dangling_start_marker_is_treated_as_no_block()
    {
        const string Existing = "# 제목\n<!-- daiso:start -->\n내용\n";

        var result = _writer.Apply(Existing, Body);

        result.Should().StartWith(Existing);
        result.Should().Contain(InstructionMarkerWriter.EndMarker);
    }
}

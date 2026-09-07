namespace Daiso.Core.Tests.Rules;

/// <summary>REQUIREMENTS §6.4 양식과 ARCHITECTURE §2.1 괄호 규칙.</summary>
public sealed class MarkdownRuleRendererTests
{
    private readonly MarkdownRuleRenderer _renderer = new();
    private readonly RulePresetSerializer _serializer = new();

    [Fact]
    public void Renders_the_canonical_sample()
    {
        var markdown = _renderer.Render(_serializer.Parse(SampleRules.Canonical));

        markdown.Should().Be(
            """
            ## Backend Rules

            한국어로 응답한다 (SHOULD)
            비밀키/토큰 값을 출력하지 않는다 (MUST)

            ### C# 파일을 수정할 때

            변경 전 영향 범위를 먼저 설명한다 (MUST)
            관련 테스트를 함께 수정한다 (SHOULD)

            ### C# 파일을 수정할 때 & (public API 변경 | DB 스키마 변경)

            마이그레이션 계획을 먼저 작성한다 (MUST)

            ### !테스트 코드

            공개 함수에 XML 주석을 남긴다 (MAY)

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Renders_a_preset_with_no_global_actions()
    {
        var preset = new RulePreset(1, "P", null, [], [
            new Rule(new LeafCondition("조건"), [new RuleAction("행동", Priority.Must)]),
        ]);

        _renderer.Render(preset).Should().Be(
            """
            ## P

            ### 조건

            행동 (MUST)

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Renders_a_title_only_preset()
    {
        _renderer.Render(new RulePreset(1, "P", null, [], [])).Should().Be("## P\n");
    }

    // ── 괄호 규칙 5케이스 (ARCHITECTURE §8) ───────────────────────────────

    [Fact]
    public void An_or_inside_an_and_is_parenthesized()
    {
        var condition = new AndCondition([
            new LeafCondition("A"),
            new OrCondition([new LeafCondition("B"), new LeafCondition("C")]),
        ]);

        _renderer.Describe(condition).Should().Be("A & (B | C)");
    }

    [Fact]
    public void An_and_inside_an_or_is_parenthesized()
    {
        var condition = new OrCondition([
            new AndCondition([new LeafCondition("A"), new LeafCondition("B")]),
            new LeafCondition("C"),
        ]);

        _renderer.Describe(condition).Should().Be("(A & B) | C");
    }

    [Fact]
    public void A_composite_inside_a_not_is_always_parenthesized()
    {
        var condition = new NotCondition(
            new AndCondition([new LeafCondition("A"), new LeafCondition("B")]));

        _renderer.Describe(condition).Should().Be("!(A & B)");
    }

    [Fact]
    public void A_not_inside_a_not_needs_no_parentheses()
    {
        var condition = new NotCondition(new NotCondition(new LeafCondition("A")));

        _renderer.Describe(condition).Should().Be("!!A");
    }

    [Fact]
    public void A_leaf_containing_operator_characters_is_quoted()
    {
        var condition = new OrCondition([
            new LeafCondition("A & B 케이스"),
            new LeafCondition("C"),
        ]);

        _renderer.Describe(condition).Should().Be("\"A & B 케이스\" | C");
    }

    // ── 그 밖의 괄호 동작 ─────────────────────────────────────────────────

    [Fact]
    public void The_same_operator_nested_needs_no_parentheses()
    {
        var condition = new AndCondition([
            new LeafCondition("A"),
            new AndCondition([new LeafCondition("B"), new LeafCondition("C")]),
        ]);

        _renderer.Describe(condition).Should().Be("A & B & C");
    }

    [Fact]
    public void A_not_inside_an_and_needs_no_parentheses()
    {
        var condition = new AndCondition([
            new LeafCondition("A"),
            new NotCondition(new LeafCondition("B")),
        ]);

        _renderer.Describe(condition).Should().Be("A & !B");
    }

    [Fact]
    public void A_single_item_operator_renders_without_parentheses()
    {
        _renderer.Describe(new AndCondition([new LeafCondition("A")])).Should().Be("A");
        _renderer.Describe(new OrCondition([new LeafCondition("A")])).Should().Be("A");
    }

    [Theory]
    [InlineData("A|B")]
    [InlineData("A!B")]
    [InlineData("(A)")]
    [InlineData("A)B")]
    public void Every_operator_character_triggers_quoting(string text)
    {
        _renderer.Describe(new LeafCondition(text)).Should().Be($"\"{text}\"");
    }

    [Fact]
    public void A_plain_leaf_is_not_quoted()
    {
        _renderer.Describe(new LeafCondition("C# 파일을 수정할 때")).Should().Be("C# 파일을 수정할 때");
    }
}

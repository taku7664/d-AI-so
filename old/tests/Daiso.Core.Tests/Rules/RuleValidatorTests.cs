namespace Daiso.Core.Tests.Rules;

/// <summary>ARCHITECTURE §2.1 검증 규칙 표. 오류 행마다 케이스 하나 이상.</summary>
public sealed class RuleValidatorTests
{
    private readonly RulePresetSerializer _serializer = new();

    // ── 오류 행 ──────────────────────────────────────────────────────────

    [Fact]
    public void Missing_daiso_is_an_error()
    {
        var error = Rejects("""
            name: P
            global: []
            rules: []
            """);

        error.Detail.Should().Contain("daiso");
        error.Line.Should().Be(1);
        error.Column.Should().Be(1);
    }

    [Fact]
    public void A_schema_version_other_than_one_is_an_error()
    {
        var error = Rejects("""
            daiso: 2
            name: P
            """);

        error.Detail.Should().Contain("스키마 버전");
        error.Line.Should().Be(1);
        error.Column.Should().Be(8);
    }

    [Fact]
    public void Missing_name_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            rules: []
            """);

        error.Detail.Should().Contain("name");
        error.Line.Should().Be(1);
    }

    [Fact]
    public void An_empty_name_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: "   "
            """);

        error.Detail.Should().Contain("빈 문자열");
        error.Line.Should().Be(2);
        error.Column.Should().Be(7);
    }

    [Fact]
    public void A_condition_map_with_no_key_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when: {}
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("정확히 하나");
        error.Line.Should().Be(4);
        error.Column.Should().Be(11);
    }

    [Fact]
    public void A_condition_map_with_two_keys_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when:
                  and:
                    - A
                  or:
                    - B
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("정확히 하나");
        error.Line.Should().Be(5);
        error.Column.Should().Be(7);
    }

    [Fact]
    public void An_unknown_top_level_key_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            extra: 값
            """);

        error.Detail.Should().Contain("알 수 없는 키 'extra'");
        error.Line.Should().Be(3);
        error.Column.Should().Be(1);
    }

    [Fact]
    public void An_unknown_rule_key_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when: A
                then:
                  - action: 행동
                note: 메모
            """);

        error.Detail.Should().Contain("알 수 없는 키 'note'");
        error.Line.Should().Be(7);
    }

    [Fact]
    public void An_unknown_action_key_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            global:
              - action: 행동
                weight: 3
            """);

        error.Detail.Should().Contain("알 수 없는 키 'weight'");
        error.Line.Should().Be(5);
    }

    [Fact]
    public void An_unknown_condition_operator_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when:
                  xor:
                    - A
                    - B
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("알 수 없는 조건 연산자 'xor'");
        error.Line.Should().Be(5);
    }

    [Fact]
    public void An_empty_and_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when:
                  and: []
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("'and' 항목이 하나도 없다");
        error.Line.Should().Be(5);
    }

    [Fact]
    public void An_empty_or_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when:
                  or: []
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("'or' 항목이 하나도 없다");
        error.Line.Should().Be(5);
    }

    [Fact]
    public void A_blank_leaf_condition_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when: "   "
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("조건 문장이 비어 있다");
        error.Line.Should().Be(4);
    }

    [Fact]
    public void A_blank_leaf_inside_an_operator_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when:
                  and:
                    - A
                    - "  "
                then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("조건 문장이 비어 있다");
        error.Line.Should().Be(7);
    }

    [Fact]
    public void An_empty_then_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when: A
                then: []
            """);

        error.Detail.Should().Contain("행동이 하나도 없다");
        error.Line.Should().Be(5);
    }

    [Fact]
    public void A_missing_then_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - when: A
            """);

        error.Detail.Should().Contain("'then' 키가 없다");
        error.Line.Should().Be(4);
    }

    [Fact]
    public void A_missing_when_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            rules:
              - then:
                  - action: 행동
            """);

        error.Detail.Should().Contain("'when' 키가 없다");
        error.Line.Should().Be(4);
    }

    [Fact]
    public void A_blank_action_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            global:
              - action: "  "
            """);

        error.Detail.Should().Contain("'action' 문장이 비어 있다");
        error.Line.Should().Be(4);
    }

    [Fact]
    public void An_unknown_priority_is_an_error()
    {
        var error = Rejects("""
            daiso: 1
            name: P
            global:
              - action: 행동
                priority: MAYBE
            """);

        error.Detail.Should().Contain("MUST, SHOULD, MAY");
        error.Line.Should().Be(5);
        error.Column.Should().Be(15);
    }

    // ── 허용 행 ──────────────────────────────────────────────────────────

    [Fact]
    public void A_single_item_and_is_allowed()
    {
        var preset = _serializer.Parse("""
            daiso: 1
            name: P
            rules:
              - when:
                  and:
                    - A
                then:
                  - action: 행동
            """);

        preset.Rules[0].When.Should().BeOfType<AndCondition>()
            .Which.Items.Should().Equal(new LeafCondition("A"));
    }

    [Fact]
    public void A_single_item_or_is_allowed()
    {
        var preset = _serializer.Parse("""
            daiso: 1
            name: P
            rules:
              - when:
                  or:
                    - A
                then:
                  - action: 행동
            """);

        preset.Rules[0].When.Should().BeOfType<OrCondition>()
            .Which.Items.Should().Equal(new LeafCondition("A"));
    }

    [Fact]
    public void The_not_operator_is_rejected()
    {
        // 부정은 형식에서 지원하지 않는다. 문장으로 쓴다. (REQUIREMENTS §6.3)
        var act = () => _serializer.Parse("""
            daiso: 1
            name: P
            rules:
              - when:
                  not: A
                then:
                  - action: 행동
            """);

        act.Should().Throw<RuleParseException>().Which.Detail.Should().Contain("not");
    }

    [Fact]
    public void Global_and_rules_may_be_omitted()
    {
        var preset = _serializer.Parse("""
            daiso: 1
            name: P
            """);

        preset.Global.Should().BeEmpty();
        preset.Rules.Should().BeEmpty();
    }

    private RuleParseException Rejects(string yaml) =>
        Assert.Throws<RuleParseException>(() => _serializer.Parse(yaml));
}

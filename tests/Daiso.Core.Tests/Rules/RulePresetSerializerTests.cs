namespace Daiso.Core.Tests.Rules;

/// <summary>ARCHITECTURE §2.1 YAML 매핑·라운드트립 규칙.</summary>
public sealed class RulePresetSerializerTests
{
    private readonly RulePresetSerializer _serializer = new();

    [Fact]
    public void Serialize_is_idempotent_for_the_canonical_sample()
    {
        var once = _serializer.Serialize(_serializer.Parse(SampleRules.Canonical));
        var twice = _serializer.Serialize(_serializer.Parse(once));

        twice.Should().Be(once);
    }

    [Fact]
    public void Parse_reads_the_canonical_sample_structure()
    {
        var preset = _serializer.Parse(SampleRules.Canonical);

        preset.Daiso.Should().Be(1);
        preset.Name.Should().Be("Backend Rules");
        preset.Description.Should().Be("서버 코드 작업 시 규칙");
        preset.Global.Should().HaveCount(2);
        preset.Global[0].Should().Be(new RuleAction("한국어로 응답한다", Priority.Should));
        preset.Global[1].Should().Be(new RuleAction("비밀키/토큰 값을 출력하지 않는다", Priority.Must));
        preset.Rules.Should().HaveCount(3);

        preset.Rules[0].When.Should().Be(new LeafCondition("C# 파일을 수정할 때"));
        preset.Rules[0].Then.Should().HaveCount(2);

        var and = preset.Rules[1].When.Should().BeOfType<AndCondition>().Subject;
        and.Items.Should().HaveCount(2);
        and.Items[0].Should().Be(new LeafCondition("C# 파일을 수정할 때"));
        var or = and.Items[1].Should().BeOfType<OrCondition>().Subject;
        or.Items.Should().Equal(new LeafCondition("public API 변경"), new LeafCondition("DB 스키마 변경"));

        preset.Rules[2].When.Should().Be(new LeafCondition("테스트 코드가 아닐 때"));
    }

    [Fact]
    public void Omitted_priority_becomes_Should()
    {
        const string Yaml = """
            daiso: 1
            name: P
            rules:
              - when: 조건
                then:
                  - action: 행동
            """;

        var preset = _serializer.Parse(Yaml);

        preset.Rules[0].Then[0].Priority.Should().Be(Priority.Should);
    }

    [Fact]
    public void Priority_is_always_written_even_when_Should()
    {
        var preset = new RulePreset(1, "P", null, [], [
            new Rule(new LeafCondition("조건"), [new RuleAction("행동")]),
        ]);

        var yaml = _serializer.Serialize(preset);

        yaml.Should().Contain("priority: SHOULD");
    }

    [Theory]
    [InlineData("MUST", Priority.Must)]
    [InlineData("must", Priority.Must)]
    [InlineData("Should", Priority.Should)]
    [InlineData("mAy", Priority.May)]
    public void Priority_parsing_ignores_case(string text, Priority expected)
    {
        var yaml = $"""
            daiso: 1
            name: P
            global:
              - action: 행동
                priority: {text}
            """;

        _serializer.Parse(yaml).Global[0].Priority.Should().Be(expected);
    }

    [Fact]
    public void Serialize_uses_a_fixed_key_order()
    {
        var preset = new RulePreset(1, "이름", "설명", [new RuleAction("전역 행동", Priority.May)], [
            new Rule(new LeafCondition("조건"), [new RuleAction("행동", Priority.Must)]),
        ]);

        var yaml = _serializer.Serialize(preset);

        yaml.Should().Be(
            """
            daiso: 1
            name: 이름
            description: 설명
            global:
              - action: 전역 행동
                priority: MAY
            rules:
              - when: 조건
                then:
                  - action: 행동
                    priority: MUST

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Serialize_omits_a_null_description()
    {
        var yaml = _serializer.Serialize(new RulePreset(1, "이름", null, [], []));

        yaml.Should().NotContain("description");
        yaml.Should().Contain("name: 이름");
    }

    [Fact]
    public void Serialize_keeps_an_empty_description()
    {
        var yaml = _serializer.Serialize(new RulePreset(1, "이름", string.Empty, [], []));

        yaml.Should().Contain("description:");
    }

    [Fact]
    public void Nested_conditions_round_trip()
    {
        var preset = new RulePreset(1, "P", null, [], [
            new Rule(
                new AndCondition([
                    new LeafCondition("A"),
                    new OrCondition([new LeafCondition("B"), new LeafCondition("C")]),
                ]),
                [new RuleAction("행동", Priority.Must)]),
        ]);

        var once = _serializer.Serialize(preset);
        var twice = _serializer.Serialize(_serializer.Parse(once));

        twice.Should().Be(once);
    }

    [Fact]
    public void Leaf_text_with_operator_characters_round_trips()
    {
        var preset = new RulePreset(1, "P", null, [], [
            new Rule(new LeafCondition("A & B | !C (특수)"), [new RuleAction("행동: 콜론 포함", Priority.May)]),
        ]);

        var once = _serializer.Serialize(preset);
        var reparsed = _serializer.Parse(once);

        reparsed.Rules[0].When.Should().Be(new LeafCondition("A & B | !C (특수)"));
        reparsed.Rules[0].Then[0].Action.Should().Be("행동: 콜론 포함");
        _serializer.Serialize(reparsed).Should().Be(once);
    }

    [Fact]
    public void Parse_rejects_a_malformed_document_with_a_position()
    {
        const string Yaml = """
            daiso: 1
            name: P
            global:
              - action: 행동
               priority: MUST
            """;

        var act = () => _serializer.Parse(Yaml);

        act.Should().Throw<RuleParseException>()
            .Which.Line.Should().BeGreaterThan(0);
    }
}

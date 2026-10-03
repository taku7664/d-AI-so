namespace Daiso.Core.Tests.Rules;

/// <summary>ARCHITECTURE §5.2 — 기본 제공 프리셋은 사용자 파일과 같은 형식이고, 전부 열리고, 서로 이름이 겹치지 않는다.</summary>
public sealed class BuiltInPresetsTests
{
    private readonly RulePresetSerializer _serializer = new();
    private readonly MarkdownRuleRenderer _renderer = new();

    [Fact]
    public void There_are_at_least_ten_presets()
    {
        BuiltInPresets.Ids.Should().HaveCountGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_catalog_and_the_embedded_files_match_exactly()
    {
        BuiltInPresets.Ids.Order(StringComparer.Ordinal)
            .Should().Equal(BuiltInPresets.EmbeddedIds);
    }

    [Fact]
    public void Every_preset_parses_validates_and_round_trips()
    {
        foreach (var id in BuiltInPresets.Ids)
        {
            var text = BuiltInPresets.Read(id);
            var once = _serializer.Serialize(_serializer.Parse(text));
            var twice = _serializer.Serialize(_serializer.Parse(once));

            twice.Should().Be(once, because: $"{id} 프리셋은 라운드트립이 보장되어야 한다");
        }
    }

    [Fact]
    public void Every_preset_has_a_name_a_description_and_something_to_say()
    {
        foreach (var item in BuiltInPresets.List(_serializer))
        {
            item.Name.Should().NotBeNullOrWhiteSpace(because: $"{item.Id}");
            item.Description.Should().NotBeNullOrWhiteSpace(because: $"{item.Id}는 갤러리에서 한 줄 설명이 보여야 한다");

            var preset = _serializer.Parse(BuiltInPresets.Read(item.Id));
            preset.Rules.Should().NotBeEmpty(because: $"{item.Id}는 조건 규칙이 하나는 있어야 한다");
            (preset.Global.Count + preset.Rules.Count).Should().BeGreaterThanOrEqualTo(3, because: $"{item.Id}는 출발점으로 쓸 만큼 내용이 있어야 한다");
        }
    }

    [Fact]
    public void Names_do_not_collide()
    {
        var names = BuiltInPresets.List(_serializer).Select(item => item.Name).ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_preset_renders_to_markdown_without_quoting_leaves()
    {
        // 리프에 & | ! ( ) 가 들어가면 렌더가 큰따옴표로 감싼다. 기본 프리셋은 그런 문장을 쓰지 않는다.
        foreach (var id in BuiltInPresets.Ids)
        {
            var markdown = _renderer.Render(_serializer.Parse(BuiltInPresets.Read(id)));

            markdown.Should().StartWith("## ");
            markdown.Should().NotContain("### \"", because: $"{id}의 조건 문장에 연산자 문자가 들어 있다");
        }
    }

    [Fact]
    public void Reading_an_unknown_id_fails_clearly()
    {
        var act = () => BuiltInPresets.Read("no-such-preset");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no-such-preset*");
    }
}

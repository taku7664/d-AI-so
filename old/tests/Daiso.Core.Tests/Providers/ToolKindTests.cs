namespace Daiso.Core.Tests.Providers;

/// <summary>
/// 도구 id 는 <b>디스크에 적히는 값</b>이다 — 인덱스의 <c>tool</c> 열, 계정 보관함의 폴더 이름.
/// 그래서 꼴을 좁게 잡고, 한 번 정하면 바꾸지 않는다. (docs/PLUGIN_PLAN.md Stage 1)
/// </summary>
public sealed class ToolKindTests
{
    [Theory]
    [InlineData("mycli")]
    [InlineData("my-cli")]
    [InlineData("cli2")]
    [InlineData("ab")]
    public void A_well_formed_id_parses(string id)
    {
        ToolKind.TryParse(id, out var kind).Should().BeTrue();
        kind.Id.Should().Be(id);
    }

    [Fact]
    public void Case_does_not_matter_but_the_stored_value_is_lower()
    {
        // 옛 기록에는 enum 이름 그대로 "Claude" 가 적혀 있다. 그것도 읽어야 한다
        ToolKind.TryParse("Claude", out var kind).Should().BeTrue();
        kind.Should().Be(ToolKind.Claude);
        kind.Id.Should().Be("claude");
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("99")]
    [InlineData("2cli")]
    public void An_id_that_does_not_start_with_a_letter_is_rejected(string id)
    {
        // 주석에는 이 규칙이 적혀 있었는데 검사에 없었다 (2026-09-11 점검).
        // 이 값이 폴더 이름이 되므로 숫자·기호로 시작하면 읽기 어렵고, 도구를 바꿔치기하는 이름도 만들기 쉽다
        ToolKind.TryParse(id, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("my cli")]
    [InlineData("my/cli")]
    [InlineData("my.cli")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_malformed_id_is_rejected(string? id)
    {
        ToolKind.TryParse(id, out _).Should().BeFalse();
    }

    [Fact]
    public void Of_throws_on_a_malformed_id()
    {
        var act = () => ToolKind.Of("99");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_built_in_tools_are_the_three_we_ship()
    {
        ToolKind.BuiltIn.Should().Equal(ToolKind.Claude, ToolKind.Codex, ToolKind.Antigravity);
    }

    [Fact]
    public void The_default_value_points_at_nothing()
    {
        default(ToolKind).IsEmpty.Should().BeTrue();
        default(ToolKind).Id.Should().BeEmpty();
    }
}

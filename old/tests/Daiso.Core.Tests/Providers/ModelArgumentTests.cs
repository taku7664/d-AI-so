namespace Daiso.Core.Tests.Providers;

/// <summary>
/// 모델 고르기가 인자 칸의 <c>--model</c> 을 고쳐 쓴다. 다른 인자를 망가뜨리지 않는지 잠근다.
/// </summary>
public sealed class ModelArgumentTests
{
    [Theory]
    [InlineData("", "opus", "--model opus")]
    [InlineData("--sandbox", "opus", "--sandbox --model opus")]
    [InlineData("--model sonnet --sandbox", "opus", "--sandbox --model opus")]
    [InlineData("--permission-mode plan --model=sonnet", "opus", "--permission-mode plan --model opus")]
    [InlineData("--model --sandbox", "opus", "--sandbox --model opus")]
    [InlineData("a --model --sandbox", null, "a --sandbox")]
    [InlineData("--model sonnet", null, "")]
    [InlineData("--model sonnet --model fable", "opus", "--model opus")]
    [InlineData("--models x", "opus", "--models x --model opus")]
    [InlineData("--sandbox", "claude-fable-5-1[1m]", "--sandbox --model \"claude-fable-5-1[1m]\"")]
    public void Apply_leaves_exactly_one_model_flag(string before, string? model, string after)
    {
        ModelArgument.Apply(before, model).Should().Be(after);
    }

    [Theory]
    [InlineData("--sandbox", null)]
    [InlineData("--model opus", "opus")]
    [InlineData("--model=gpt-5.5 --sandbox", "gpt-5.5")]
    [InlineData("--model \"claude-fable-5-1[1m]\"", "claude-fable-5-1[1m]")]
    [InlineData("--model --sandbox", null)]
    [InlineData("--model sonnet --model opus", "opus")]
    [InlineData("--models x", null)]
    public void Read_gives_the_model_the_cli_will_use(string arguments, string? expected)
    {
        ModelArgument.Read(arguments).Should().Be(expected);
    }

    [Theory]
    [InlineData("opus")]
    [InlineData("gemini-3.8-flash-high")]
    [InlineData("claude-fable-5-1[1m]")]
    public void What_apply_writes_read_gives_back(string model)
    {
        ModelArgument.Read(ModelArgument.Apply("--sandbox", model)).Should().Be(model);
    }
}

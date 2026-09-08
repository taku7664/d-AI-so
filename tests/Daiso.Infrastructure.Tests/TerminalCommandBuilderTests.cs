namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.3 — 항상 셸로 감싸고, wt는 있으면 쓴다.</summary>
public sealed class TerminalCommandBuilderTests
{
    private const string WorkingDir = @"C:\Fixture\Project";

    [Fact]
    public void Without_wt_the_shell_runs_directly_with_pwsh()
    {
        var command = Builder("pwsh").Build(WorkingDir, "claude", string.Empty);

        command.FileName.Should().Be("pwsh");
        command.Arguments.Should().Be("-NoExit -Command \"& claude\"");
    }

    [Fact]
    public void Without_pwsh_it_falls_back_to_powershell()
    {
        var command = Builder("powershell").Build(WorkingDir, "claude", string.Empty);

        command.FileName.Should().Be("powershell");
        command.Arguments.Should().Be("-NoExit -Command \"& claude\"");
    }

    [Fact]
    public void Without_any_powershell_it_falls_back_to_cmd()
    {
        var command = Builder().Build(WorkingDir, "codex", string.Empty);

        command.FileName.Should().Be("cmd");
        command.Arguments.Should().Be("/k codex");
    }

    [Fact]
    public void With_wt_the_shell_command_is_wrapped_in_a_wt_tab()
    {
        var command = Builder("wt", "pwsh").Build(WorkingDir, "claude", "--resume abc");

        command.FileName.Should().Be("wt");
        command.Arguments.Should().Be(
            "-d C:\\Fixture\\Project pwsh -NoExit -Command \"& claude --resume abc\"");
    }

    [Fact]
    public void With_wt_and_only_cmd_available()
    {
        var command = Builder("wt").Build(WorkingDir, "codex", "resume abc");

        command.FileName.Should().Be("wt");
        command.Arguments.Should().Be("-d C:\\Fixture\\Project cmd /k codex resume abc");
    }

    [Fact]
    public void A_working_directory_with_spaces_is_quoted()
    {
        var command = Builder("wt", "pwsh").Build(@"C:\My Projects\App", "claude", string.Empty);

        command.Arguments.Should().StartWith("-d \"C:\\My Projects\\App\"");
    }

    [Fact]
    public void Arguments_are_carried_into_the_shell_invocation()
    {
        var command = Builder("pwsh").Build(WorkingDir, "claude", "--resume 1234 --verbose");

        command.Arguments.Should().Be("-NoExit -Command \"& claude --resume 1234 --verbose\"");
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void The_tool_is_never_invoked_without_a_shell(string tool)
    {
        foreach (var available in new[] { Array.Empty<string>(), ["pwsh"], ["powershell"], ["wt", "pwsh"] })
        {
            var command = new TerminalCommandBuilder(name => available.Contains(name))
                .Build(WorkingDir, tool, string.Empty);

            command.FileName.Should().BeOneOf("wt", "pwsh", "powershell", "cmd");
            command.FileName.Should().NotBe(tool);
        }
    }

    [Fact]
    public void A_blank_working_directory_is_rejected()
    {
        var act = () => Builder("pwsh").Build("  ", "claude", string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    private static TerminalCommandBuilder Builder(params string[] available) =>
        new(name => available.Contains(name, StringComparer.Ordinal));

    [Fact]
    public void Quoted_first_message_is_escaped_for_powershell_but_left_alone_for_cmd()
    {
        var pwsh = Builder("pwsh").BuildShellCommand("claude", "\"docs/prompts/x.md 파일을 읽고 진행\"");
        var cmd = Builder().BuildShellCommand("gemini", "-i \"docs/prompts/x.md 파일을 읽고 진행\"");

        pwsh.Arguments.Should().Be("-NoExit -Command \"& claude \\\"docs/prompts/x.md 파일을 읽고 진행\\\"\"");
        cmd.Arguments.Should().Be("/k gemini -i \"docs/prompts/x.md 파일을 읽고 진행\"");
    }
}

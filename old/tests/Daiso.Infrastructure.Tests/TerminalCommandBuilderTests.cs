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

    /// <summary>
    /// 첫 메시지는 <b>글</b>이다. PowerShell 은 큰따옴표 안에서도 <c>$(…)</c> 를 풀어 읽으므로
    /// 그대로 넣으면 프롬프트에 적힌 명령이 실행됐다 (2026-09-11 점검).
    /// </summary>
    [Fact]
    public void A_first_message_cannot_run_commands()
    {
        var command = Builder("pwsh").BuildShellCommand("claude", "\"$(Remove-Item) 를 설명해줘\"");

        command.Arguments.Should().Contain("`$(Remove-Item", because: "$ 를 죽여야 안이 실행되지 않는다");
        command.Arguments.Should().NotContain(" $(Remove-Item");
    }

    [Fact]
    public void A_first_message_with_a_backtick_keeps_it_as_text()
    {
        var command = Builder("pwsh").BuildShellCommand("claude", "\"`code` 를 보라\"");

        command.Arguments.Should().Contain("``code``");
    }

    /// <summary>
    /// 따옴표 <b>밖</b>의 셸 문법은 일부러 들어온다. Antigravity 설치가 파이프에 기대고 있다 —
    /// 안을 지키는 일이 밖을 망가뜨리면 안 된다.
    /// </summary>
    [Fact]
    public void Shell_syntax_outside_quotes_survives()
    {
        var command = Builder("pwsh").BuildShellCommand("irm", "https://antigravity.google/cli/install.ps1 | iex");

        command.Arguments.Should().Be("-NoExit -Command \"& irm https://antigravity.google/cli/install.ps1 | iex\"");
    }

    /// <summary>wt 는 <c>;</c> 를 탭 구분자로 읽는다. 프롬프트의 세미콜론에서 명령이 잘렸다.</summary>
    [Fact]
    public void A_semicolon_does_not_split_the_windows_terminal_command()
    {
        var command = Builder("wt", "pwsh").Build(WorkingDir, "claude", "\"먼저 빌드; 그다음 테스트\"");

        command.FileName.Should().Be("wt");
        command.Arguments.Should().Contain(@"빌드\; 그다음");
    }

    private static TerminalCommandBuilder Builder(params string[] available) =>
        new(name => available.Contains(name, StringComparer.Ordinal));

    [Fact]
    public void Quoted_first_message_is_escaped_for_powershell_but_left_alone_for_cmd()
    {
        var pwsh = Builder("pwsh").BuildShellCommand("claude", "\"docs/prompts/x.md 파일을 읽고 진행\"");
        var cmd = Builder().BuildShellCommand("agy", "-i \"docs/prompts/x.md 파일을 읽고 진행\"");

        pwsh.Arguments.Should().Be("-NoExit -Command \"& claude \\\"docs/prompts/x.md 파일을 읽고 진행\\\"\"");
        cmd.Arguments.Should().Be("/k agy -i \"docs/prompts/x.md 파일을 읽고 진행\"");
    }

    /// <summary>
    /// 방금 깐 도구는 PATH 에 아직 없어 제공자가 절대 경로를 준다(<c>IProvider.LaunchTarget</c>).
    /// 사용자 이름에 빈칸이 있으면 경로가 두 토큰으로 쪼개져 셸이 엉뚱한 것을 실행하므로 따옴표로 감싼다.
    /// </summary>
    [Fact]
    public void A_command_path_with_spaces_is_quoted()
    {
        const string path = @"C:\Users\Hong Gildong\AppData\Local\agy\bin\agy.exe";

        var pwsh = Builder("pwsh").BuildShellCommand(path, "--continue");
        var cmd = Builder().BuildShellCommand(path, "--continue");

        pwsh.Arguments.Should().Be($"-NoExit -Command \"& \\\"{path}\\\" --continue\"");
        cmd.Arguments.Should().Be($"/k \"{path}\" --continue");
    }

    /// <summary>
    /// Antigravity 설치 명령은 PowerShell 문법(`irm … | iex`)이다. 첫 빈칸에서 쪼개 셸로 감싸도 파이프가 살아 있어야 한다 —
    /// `&amp; irm <url> | iex` 는 PowerShell 에서 `(&amp; irm <url>) | iex` 로 읽힌다. 이스케이프를 손보다 파이프를 잃으면 설치가 조용히 실패한다.
    /// </summary>
    [Fact]
    public void The_antigravity_install_command_keeps_its_pipe_when_wrapped_in_powershell()
    {
        // 문구의 정본은 AntigravityProvider.InstallCommand 이고, 그 값 자체는 InstallCommandTests 가 잠근다.
        // 여기서는 같은 문자열이 셸 포장을 지나도 성립하는지만 본다(이 테스트 프로젝트는 제공자를 참조하지 않는다)
        const string install = "irm https://antigravity.google/cli/install.ps1 | iex";
        var space = install.IndexOf(' ', StringComparison.Ordinal);

        var wrapped = Builder("pwsh").BuildShellCommand(install[..space], install[(space + 1)..]);

        wrapped.FileName.Should().Be("pwsh");
        wrapped.Arguments.Should().Be("-NoExit -Command \"& irm https://antigravity.google/cli/install.ps1 | iex\"");
    }
}

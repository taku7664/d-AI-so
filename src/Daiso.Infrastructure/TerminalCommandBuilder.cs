namespace Daiso.Infrastructure;

/// <summary>
/// 터미널 실행 명령 문자열을 만든다. 프로세스 실행과 분리해 두어 테스트할 수 있다. (ARCHITECTURE §5.3)
/// </summary>
/// <remarks>
/// claude / codex는 npm이 만든 `.cmd` 셸 래퍼라 항상 셸로 감싸야 한다.
/// `wt.exe`가 있으면 Windows Terminal 탭으로 열고, 없으면 셸을 새 창으로 직접 띄운다.
/// wt 없음이 기본 경로다.
/// </remarks>
public sealed class TerminalCommandBuilder
{
    /// <summary>PATH에 실행 파일이 있는지 판정한다. 테스트에서 갈아끼운다.</summary>
    private readonly Func<string, bool> _existsOnPath;

    public TerminalCommandBuilder(Func<string, bool> existsOnPath)
    {
        ArgumentNullException.ThrowIfNull(existsOnPath);
        _existsOnPath = existsOnPath;
    }

    /// <summary>실행할 파일과 인자.</summary>
    public sealed record TerminalCommand(string FileName, string Arguments);

    /// <summary>
    /// <paramref name="command"/>(claude | codex)를 <paramref name="workingDir"/>에서 실행하는 명령.
    /// </summary>
    public TerminalCommand Build(string workingDir, string command, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var shell = BuildShell(command, arguments ?? string.Empty);

        return _existsOnPath("wt")
            ? new TerminalCommand("wt", $"-d {Quote(workingDir)} {shell.FileName} {shell.Arguments}")
            : shell;
    }

    /// <summary>
    /// 새 창 없이 셸 명령만. 내장 터미널(의사 콘솔)은 wt를 거치지 않고 이 명령을 그대로 띄운다.
    /// </summary>
    public TerminalCommand BuildShellCommand(string command, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return BuildShell(command, arguments ?? string.Empty);
    }

    /// <summary>pwsh → powershell → cmd 순으로 고른다.</summary>
    private TerminalCommand BuildShell(string command, string arguments)
    {
        var invocation = arguments.Length == 0 ? command : $"{command} {arguments}";

        if (_existsOnPath("pwsh"))
        {
            return new TerminalCommand("pwsh", $"-NoExit -Command \"& {invocation}\"");
        }

        if (_existsOnPath("powershell"))
        {
            return new TerminalCommand("powershell", $"-NoExit -Command \"& {invocation}\"");
        }

        return new TerminalCommand("cmd", $"/k {invocation}");
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}

using System.Text;

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
            ? new TerminalCommand("wt", $"-d {Quote(workingDir)} {shell.FileName} {ForWindowsTerminal(shell.Arguments)}")
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

    /// <summary>
    /// pwsh → powershell → cmd 순으로 고른다. 인자 안의 큰따옴표(예: 첫 메시지 <c>"docs/prompts/x.md 파일을 읽고…"</c>)는
    /// PowerShell 경로에서 <c>-Command "…"</c> 바깥 따옴표와 겹치므로 <c>\"</c>로 이스케이프한다. cmd는 그대로 통과한다.
    /// </summary>
    private TerminalCommand BuildShell(string command, string arguments)
    {
        // 명령이 절대 경로일 수 있다(방금 깐 도구는 PATH 에 아직 없어 제공자가 경로를 준다).
        // 사용자 이름에 빈칸이 있으면 `C:\Users\Hong Gildong\…\agy.exe` 가 두 토큰으로 쪼개진다
        var target = command.Contains(' ', StringComparison.Ordinal) ? Quote(command) : command;
        var invocation = arguments.Length == 0 ? target : $"{target} {arguments}";
        var forPowerShell = Literalize(invocation).Replace("\"", "\\\"", StringComparison.Ordinal);

        if (_existsOnPath("pwsh"))
        {
            return new TerminalCommand("pwsh", $"-NoExit -Command \"& {forPowerShell}\"");
        }

        if (_existsOnPath("powershell"))
        {
            return new TerminalCommand("powershell", $"-NoExit -Command \"& {forPowerShell}\"");
        }

        return new TerminalCommand("cmd", $"/k {invocation}");
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    /// <summary>
    /// <c>wt</c> 는 명령줄에서 <c>;</c> 를 <b>탭 구분자</b>로 읽는다. 첫 메시지에 세미콜론이 하나만 있어도
    /// 거기서 명령이 잘려 반쪽만 실행됐다. 문서가 정한 탈출은 <c>\;</c> 다 (2026-09-11 점검).
    /// </summary>
    private static string ForWindowsTerminal(string arguments) =>
        arguments.Replace(";", "\\;", StringComparison.Ordinal);

    /// <summary>
    /// <b>따옴표 안은 글자 그대로</b> 나가게 만든다. PowerShell 은 큰따옴표 안에서도 <c>$(…)</c> 와
    /// 역따옴표를 풀어 읽으므로, 첫 메시지에 <c>$(…)</c> 가 들어 있으면 그것이 <b>실행</b>됐다.
    /// 그 글은 사람이 적은 것일 수도, 프롬프트 파일이나 남이 쓴 매니페스트에서 온 것일 수도 있다
    /// (2026-09-11 점검).
    ///
    /// <para>
    /// 따옴표 <b>밖</b>은 손대지 않는다. 거기에는 셸 문법이 일부러 들어온다 —
    /// Antigravity 설치 명령이 <c>irm … | iex</c> 이고, 파이프를 잃으면 설치가 조용히 실패한다.
    /// </para>
    /// </summary>
    private static string Literalize(string invocation)
    {
        var builder = new StringBuilder(invocation.Length);
        var inQuotes = false;

        foreach (var ch in invocation)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (inQuotes && ch is '`' or '$')
            {
                builder.Append('`');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}

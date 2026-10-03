using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 내장 터미널 화면(`Assets/xterm/index.html`)의 스크립트가 문법에 맞는가.
///
/// <para>
/// 이 파일은 <b>빌드가 봐 주지 않는다.</b> C# 이 아니라 정적 파일로 복사만 되기 때문이다.
/// 그래서 따옴표 하나가 안 닫혀도 컴파일은 통과하고, 실행하면 스크립트 전체가 돌지 않아
/// <b>터미널에 글자가 하나도 안 나온다</b> — 2026-09-10 에 실제로 그렇게 됐다.
/// 셸 heredoc 이 <c>\n</c> 의 백슬래시를 먹어 <c>join('</c> 다음에 진짜 줄바꿈이 들어간 것이 원인이었다.
/// </para>
///
/// <para>
/// 앱을 띄우기 전에 여기서 잡는다. <c>node</c> 가 있으면 진짜 파서로, 없으면 따옴표 균형만 본다.
/// </para>
/// </summary>
public sealed partial class TerminalScriptTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string PagePath =
        Path.Combine(Root, "src", "Daiso.App", "Assets", "xterm", "index.html");

    [Fact]
    public void The_terminal_page_has_exactly_one_script_block()
    {
        Scripts().Should().ContainSingle(because: "이 테스트는 그 한 덩어리를 검사한다. 늘었으면 테스트도 같이 고친다");
    }

    [Fact]
    public void No_string_literal_runs_past_the_end_of_its_line()
    {
        var offenders = new List<string>();

        foreach (var (line, number) in Scripts().Single().Split('\n').Select((text, index) => (text, index + 1)))
        {
            if (!Balanced(CommentPattern().Replace(line, string.Empty)))
            {
                offenders.Add($"{number}: {line.Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            because: "줄을 넘어가는 따옴표는 스크립트 전체를 죽인다. 그러면 터미널에 글자가 하나도 안 나온다");
    }

    [Fact]
    public void A_real_parser_accepts_the_script()
    {
        var node = Which("node");

        if (node is null)
        {
            // node 가 없는 기계에서는 위의 따옴표 검사만으로 간다. 없다고 실패시키지 않는다
            return;
        }

        var file = Path.Combine(Path.GetTempPath(), $"daiso-term-{Guid.NewGuid():N}.js");
        File.WriteAllText(file, Scripts().Single());

        try
        {
            using var process = Process.Start(new ProcessStartInfo(node, $"--check \"{file}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            process.Should().NotBeNull();
            var error = process!.StandardError.ReadToEnd();
            process.WaitForExit();

            process.ExitCode.Should().Be(0, because: $"node 가 스크립트를 거절했다:\n{error}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ── 검사기 자체가 도는지 ──────────────────────────────────────────────

    [Theory]
    [InlineData("var a = 'ok';", true)]
    [InlineData("var a = \"ok\";", true)]
    [InlineData("var a = 'it\\'s';", true)]
    [InlineData("var a = \"don't\";", true)]
    [InlineData("return lines.join('", false)]
    [InlineData("var a = 'unclosed;", false)]
    public void The_quote_check_knows_the_difference(string line, bool ok)
    {
        Balanced(line).Should().Be(ok);
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    /// <summary>한 줄 안에서 따옴표가 짝을 이루는가. 이스케이프된 따옴표는 세지 않는다.</summary>
    internal static bool Balanced(string line)
    {
        char? open = null;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\\')
            {
                i++;   // 다음 글자는 이스케이프된 것이라 건너뛴다
                continue;
            }

            if (c is not ('\'' or '"'))
            {
                continue;
            }

            if (open is null)
            {
                open = c;
            }
            else if (open == c)
            {
                open = null;
            }
        }

        return open is null;
    }

    private static IReadOnlyList<string> Scripts() =>
    [
        .. ScriptPattern().Matches(File.ReadAllText(PagePath)).Select(match => match.Groups[1].Value),
    ];

    private static string? Which(string tool)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

        return paths
            .SelectMany(folder => new[] { Path.Combine(folder, tool + ".exe"), Path.Combine(folder, tool) })
            .FirstOrDefault(File.Exists);
    }

    [GeneratedRegex(@"<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"//.*$")]
    private static partial Regex CommentPattern();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Daiso.sln 이 있는 저장소 루트를 찾지 못했다");
    }
}

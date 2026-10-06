// Claude Code 상태줄 명령 (https://code.claude.com/docs/en/statusline).
// Claude 가 stdin 으로 넘기는 JSON 에서 rate_limits(five_hour·seven_day)만 골라 {자료 폴더}\limits\claude.json 에 남긴다.
// 사용자가 원래 쓰던 상태줄이 있으면 그 명령을 같은 입력으로 돌려 출력을 그대로 넘긴다(감싸기).
//
// 무슨 일이 있어도 조용히 0 으로 끝난다. 여기서 실패해도 Claude 화면이 깨지면 안 된다.
// 로그인 토큰은 보지도 쓰지도 않는다. stdin 에 토큰은 오지 않는다.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daiso.StatusLine;

internal static class Program
{
    /// <summary>원래 상태줄을 기다리는 최대 시간. Claude 는 느린 상태줄을 기다리지 않고 다음 갱신으로 넘어간다.</summary>
    private static readonly TimeSpan OriginalTimeout = TimeSpan.FromSeconds(2);

    private static int Main(string[] args)
    {
        try
        {
            var data = DataDirectory(args);
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var input = stdin.ReadToEnd();

            TrySaveLimits(input, data);

            if (Files.ReadOriginal(data) is { } original)
            {
                RunOriginal(original, input);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 상태줄은 조용해야 한다
        }

        return 0;
    }

    /// <summary><c>--data {폴더}</c>. Host 가 등록할 때 자기 자료 폴더를 넣어 준다. 없으면 기본 자료 폴더.</summary>
    private static string DataDirectory(string[] args)
    {
        var at = Array.IndexOf(args, "--data");
        return at >= 0 && at + 1 < args.Length
            ? args[at + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAIso");
    }

    internal static void TrySaveLimits(string input, string data)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(input);
        }
        catch (JsonException)
        {
            return;
        }

        // 구독자가 아니거나 첫 응답 전이면 rate_limits 가 없다. 그때는 마지막 값을 지우지 않고 둔다
        if (root?["rate_limits"] is not JsonObject limits)
        {
            return;
        }

        var saved = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["rate_limits"] = limits.DeepClone(),
        };

        Files.WriteAtomic(Files.LimitsPath(data), saved.ToJsonString());
    }

    private static void RunOriginal(string command, string input)
    {
        Process? process;
        try
        {
            process = Process.Start(Redirected(Shell(command, preferBash: true)));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // bash 가 없으면 cmd 로 한 번 더
            process = Process.Start(Redirected(Shell(command, preferBash: false)));
        }

        if (process is null)
        {
            return;
        }

        using (process)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(OriginalTimeout))
            {
                process.Kill(entireProcessTree: true);
                return;
            }

            using var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.UTF8.GetBytes(output.GetAwaiter().GetResult());
            stdout.Write(bytes);
        }
    }

    private static ProcessStartInfo Redirected(ProcessStartInfo start)
    {
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardInputEncoding = new UTF8Encoding(false);
        return start;
    }

    /// <summary>
    /// 원래 명령은 Claude 가 돌리던 셸에서 돌린다. Windows 의 Claude Code 는 Git Bash 를 쓰므로 bash 가 먼저다.
    /// <c>CLAUDE_CODE_GIT_BASH_PATH</c> 가 있으면 그 bash 를 쓴다.
    /// </summary>
    private static ProcessStartInfo Shell(string command, bool preferBash)
    {
        if (preferBash)
        {
            var bash = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } path ? path : "bash";
            return new ProcessStartInfo(bash) { ArgumentList = { "-c", command } };
        }

        return new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/s", "/c", command } };
    }
}

/// <summary>Host 와 같이 쓰는 파일 이름과 쓰기 규칙. Host 쪽 <c>ClaudeStatusLine</c> 이 같은 이름을 쓴다.</summary>
internal static class Files
{
    public const string Folder = "limits";
    public const string LimitsFile = "claude.json";
    public const string OriginalFile = "claude-statusline-original.json";

    public static string LimitsPath(string data) => Path.Combine(data, Folder, LimitsFile);

    /// <summary>감쌀 원래 명령. 없으면 null.</summary>
    public static string? ReadOriginal(string data)
    {
        var path = Path.Combine(data, Folder, OriginalFile);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonNode.Parse(File.ReadAllText(path))?["statusLine"]?["command"]?.GetValue<string>();
    }

    /// <summary>임시 파일에 다 쓰고 자리를 바꾼다. Host 가 읽는 도중에 반쪽 파일을 보지 않게.</summary>
    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}

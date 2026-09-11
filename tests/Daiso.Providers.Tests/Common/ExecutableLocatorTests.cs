using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// ARCHITECTURE §5.3 — npm 도구는 셸에 <c>.cmd</c> 절대 경로로 넘긴다.
/// PowerShell 은 이름만 받으면 <c>.ps1</c> 을 고르고, Windows 기본 실행 정책은 그 스크립트를 거부한다.
/// PATH 는 인자로 넣는다 — 프로세스 PATH 를 건드리면 나란히 도는 다른 테스트가 흔들린다.
/// </summary>
public sealed class ExecutableLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daiso-locator-" + Guid.NewGuid().ToString("N"));

    public ExecutableLocatorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Npm_launch_target_is_the_cmd_shim_even_though_a_ps1_sits_next_to_it()
    {
        var npm = MakeDir("npm", "claude", "claude.cmd", "claude.ps1");

        ExecutableLocator.NpmLaunchTarget("claude", npm).Should().Be(Path.Combine(npm, "claude.cmd"));
    }

    [Fact]
    public void Npm_launch_target_skips_a_native_exe_earlier_on_path()
    {
        // Codex 데스크톱 앱이 깐 exe 가 npm 폴더보다 앞에 있어도 npm 셸만 쓴다는 규칙
        var native = MakeDir("native", "codex.exe");
        var npm = MakeDir("npm", "codex.cmd", "codex.ps1");
        var path = string.Join(Path.PathSeparator, native, npm);

        ExecutableLocator.NpmLaunchTarget("codex", path).Should().Be(Path.Combine(npm, "codex.cmd"));
    }

    [Fact]
    public void Npm_launch_target_falls_back_to_the_bare_name_when_no_cmd_exists()
    {
        var empty = MakeDir("empty");

        ExecutableLocator.NpmLaunchTarget("claude", empty).Should().Be("claude");
    }

    [Fact]
    public void Find_still_prefers_cmd_then_exe_within_one_directory()
    {
        var dir = MakeDir("mixed", "tool.exe", "tool.cmd", "tool.ps1");

        ExecutableLocator.Find("tool", dir).Should().Be(Path.Combine(dir, "tool.cmd"));
    }

    private string MakeDir(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(dir, file), string.Empty);
        }

        return dir;
    }
}

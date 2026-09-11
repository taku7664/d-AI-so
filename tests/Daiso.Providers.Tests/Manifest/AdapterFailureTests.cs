using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

namespace Daiso.Providers.Tests.Manifest;

/// <summary>
/// 어댑터가 어긋나도 <b>앱은 산다</b> (docs/PLUGIN_PLAN.md Stage 5 완료 기준 · §10).
/// <para>
/// 이것이 바깥 프로세스로 뺀 이유다. 앱 안에 올린 DLL 이었다면 같은 상황에서 앱이 같이 죽는다.
/// </para>
/// </summary>
public sealed class AdapterFailureTests : IDisposable
{
    private readonly string _home = Fixtures.CreateTempDirectory();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private ManifestProvider Plugin(string adapterCommand)
    {
        var tools = Path.Combine(_home, "tools");
        Directory.CreateDirectory(tools);

        File.WriteAllText(
            Path.Combine(tools, "broken.yaml"),
            """
            schema: 1
            id: broken
            name: Broken CLI
            executable: broken.cmd
            install:
              command: npm install -g broken
            sessionsRoot: "{USERPROFILE}/.broken"
            resume: "--resume {id}"
            adapter:
              command: 'ADAPTER'
            """.Replace("ADAPTER", adapterCommand, StringComparison.Ordinal));

        return new ToolPluginLoader(tools, new ProviderHome(_home)).Load().Single().Provider!;
    }

    [Fact]
    public async Task An_adapter_that_will_not_start_leaves_an_empty_list_and_a_reason()
    {
        using var plugin = Plugin(@"C:\그런\파일은\없다.exe");

        var sessions = new List<SessionInfo>();

        await foreach (var session in plugin.EnumerateSessionsAsync(CancellationToken.None))
        {
            sessions.Add(session);
        }

        sessions.Should().BeEmpty(because: "못 띄웠으면 세션도 없다. 예외로 화면을 무너뜨리지 않는다");
        plugin.AdapterError.Should().NotBeNullOrWhiteSpace(because: "왜 비었는지 사람에게 말해야 한다");
    }

    [Fact]
    public async Task An_adapter_that_says_nothing_useful_does_not_crash_the_list()
    {
        // 아무 줄도 안 쓰고 바로 끝나는 프로그램. 앱은 "답을 끝내기 전에 닫혔다"로 본다
        using var plugin = Plugin("cmd /c exit");

        var sessions = new List<SessionInfo>();

        await foreach (var session in plugin.EnumerateSessionsAsync(CancellationToken.None))
        {
            sessions.Add(session);
        }

        sessions.Should().BeEmpty();
        plugin.AdapterError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_handshake_with_a_dead_adapter_fails_without_throwing()
    {
        using var plugin = Plugin(@"C:\그런\파일은\없다.exe");

        var ok = await plugin.HandshakeAsync(CancellationToken.None);

        ok.Should().BeFalse(because: "말이 안 통하면 그 도구만 오류로 내린다");
    }

    [Fact]
    public async Task Junk_on_the_wire_is_reported_not_thrown()
    {
        // JSON 이 아닌 줄을 뱉는 프로그램
        using var plugin = Plugin("cmd /c echo 이건JSON이아니다");

        var sessions = new List<SessionInfo>();

        await foreach (var session in plugin.EnumerateSessionsAsync(CancellationToken.None))
        {
            sessions.Add(session);
        }

        sessions.Should().BeEmpty();
        plugin.AdapterError.Should().NotBeNullOrWhiteSpace(because: "규정을 어긴 어댑터도 그 도구만 오류다");
    }

    [Fact]
    public void A_tool_can_say_whether_it_has_an_adapter_at_all()
    {
        using var withAdapter = Plugin("cmd /c exit");

        withAdapter.HasAdapter.Should().BeTrue(
            because: "설정 화면이 `세션 기록 읽음/못 읽음`을 이걸로 가른다");
    }
}

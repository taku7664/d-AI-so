using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Daiso.Core;
using Daiso.Host.Tabs.Terminal;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>
/// 터미널 탭 <c>/api/terminal</c>. 진짜 AI CLI 대신 cmd.exe 를 띄우는 시험용 도구로 방을 연다(ConPTY 는 진짜다).
/// </summary>
public sealed class TerminalTests : IAsyncLifetime
{
    private RunningHost _host = null!;
    private string _folder = null!;

    public async Task InitializeAsync()
    {
        _folder = Path.Combine(Path.GetTempPath(), "daiso-host-tests", "room-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _host = await RunningHost.StartAsync(extra: services => services.AddSingleton<IProvider, ShellTool>());
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 방 프로세스가 아직 폴더를 잡고 있을 수 있다
        }
    }

    [Fact]
    public void Npm_wrappers_run_through_cmd_and_keep_inner_quotes() =>
        RoomService.CommandLine(@"C:\npm\claude.cmd", "--settings \"C:/a b/s.json\"")
            .Should().Be("cmd.exe /d /s /c \"\"C:\\npm\\claude.cmd\" --settings \"C:/a b/s.json\"\"");

    [Fact]
    public void Executables_run_directly() =>
        RoomService.CommandLine(@"C:\bin\claude.exe", string.Empty).Should().Be("\"C:\\bin\\claude.exe\"");

    [Fact]
    public async Task A_room_streams_output_takes_input_and_replays_to_a_late_viewer()
    {
        var room = await OpenAsync();
        room.State.Should().Be("idle");

        using var first = await ConnectAsync(room.Id);
        await SendAsync(first, """{"t":"in","d":"echo daiso-%USERNAME:~0,0%hello\r"}""");
        (await ReadUntilAsync(first, "daiso-hello")).Should().BeTrue(because: "입력이 프로세스로 가고 출력이 화면으로 와야 한다");

        // 나중에 붙은 화면도 지난 출력을 받는다
        using var late = await ConnectAsync(room.Id);
        (await ReadUntilAsync(late, "daiso-hello")).Should().BeTrue(because: "다시 붙으면 지난 화면을 통째로 받는다");

        (await RoomsAsync()).Single().State.Should().Be("run", because: "Enter 를 보내면 일을 시작한 것으로 본다");
    }

    [Fact]
    public async Task A_hook_file_changes_the_room_state()
    {
        var room = await OpenAsync();
        var data = _host.Options.DataDirectory;
        var exe = Path.Combine(AppContext.BaseDirectory, "Daiso.StatusLine.exe");

        // 진짜 훅처럼 상태줄 실행 파일을 훅 모드로 돌린다
        using (var hook = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "--hook", "done", "--room", room.Id, "--data", data }, UseShellExecute = false, RedirectStandardInput = true })!)
        {
            hook.StandardInput.Close();
            await hook.WaitForExitAsync();
        }

        (await WaitForAsync(rooms => rooms.Single().State == "done")).Should().BeTrue();
        (await RoomsAsync()).Single().Unseen.Should().BeTrue();

        using var client = _host.Client();
        using var seen = _host.Authed(HttpMethod.Post, $"/api/terminal/rooms/{room.Id}/seen", _host.Url);
        (await client.SendAsync(seen)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RoomsAsync()).Single().Unseen.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_a_room_ends_it()
    {
        var room = await OpenAsync();

        using var client = _host.Client();
        using var close = _host.Authed(HttpMethod.Post, $"/api/terminal/rooms/{room.Id}/close", _host.Url);
        (await client.SendAsync(close)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RoomsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Bad_requests_are_refused()
    {
        (await PostOpenAsync(new OpenRoomRequest("shell", Path.Combine(_folder, "nope")))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostOpenAsync(new OpenRoomRequest("not-a-tool", _folder))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostOpenAsync(new OpenRoomRequest("shell", _folder, ResumePath: @"C:\no\such.jsonl"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_pty_socket_needs_the_same_origin()
    {
        var room = await OpenAsync();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", _host.CookieHeader);
        socket.Options.SetRequestHeader("Origin", "http://evil.example");

        var connect = () => socket.ConnectAsync(new Uri(_host.Url.Replace("http", "ws", StringComparison.Ordinal) + $"/api/terminal/rooms/{room.Id}/pty"), CancellationToken.None);

        await connect.Should().ThrowAsync<WebSocketException>(because: "다른 사이트가 방에 키를 보내면 안 된다");
    }

    // ── 도움 ──

    private async Task<RoomInfo> OpenAsync()
    {
        using var response = await PostOpenAsync(new OpenRoomRequest("shell", _folder, Arguments: "/d /q /k prompt $g"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomInfo>())!;
    }

    private async Task<HttpResponseMessage> PostOpenAsync(OpenRoomRequest body)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Post, "/api/terminal/rooms", _host.Url);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task<List<RoomInfo>> RoomsAsync()
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, "/api/terminal/rooms");
        using var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<List<RoomInfo>>())!;
    }

    private async Task<bool> WaitForAsync(Func<List<RoomInfo>, bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition(await RoomsAsync()))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private async Task<ClientWebSocket> ConnectAsync(string id)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", _host.CookieHeader);
        socket.Options.SetRequestHeader("Origin", _host.Url);
        await socket.ConnectAsync(new Uri(_host.Url.Replace("http", "ws", StringComparison.Ordinal) + $"/api/terminal/rooms/{id}/pty"), CancellationToken.None);
        return socket;
    }

    private static Task SendAsync(ClientWebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>화면 글자에 <paramref name="text"/> 가 나올 때까지 읽는다. 10초 안에 안 나오면 false.</summary>
    private static async Task<bool> ReadUntilAsync(ClientWebSocket socket, string text)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seen = new StringBuilder();
        var buffer = new byte[64 * 1024];

        try
        {
            while (!seen.ToString().Contains(text, StringComparison.Ordinal))
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                seen.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>시험용 도구: cmd.exe. 훅을 넣지 않는다(Claude·Codex 가 아니다).</summary>
    private sealed class ShellTool : IProvider
    {
        public ToolKind Kind { get; } = ToolKind.Of("shell");

        public ToolDisplay Display { get; } = new("Shell", "test", "sh", "S", ["#000000"], string.Empty, 999);

        public string SessionsRoot => Path.Combine(Path.GetTempPath(), "daiso-no-sessions");

        public string ExecutableName => "cmd.exe";

        public string LaunchTarget => Path.Combine(Environment.SystemDirectory, "cmd.exe");

        public string InstallCommand => string.Empty;

        public string RulesFileName => "RULES.md";

        public bool AppendOnlySessions => true;

        public string ImagePasteKeys => string.Empty;

        public IReadOnlyList<AuthFile> AuthFiles => [];

        public IReadOnlyList<string> ContextFilePatterns(string projectDir) => [];

        public Task<bool> IsInstalledAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct) => Task.FromResult(AuthStatus.Missing(Kind));

        public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct) => throw new NotSupportedException();

        public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(string filePath, long fromByteOffset, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public string BuildResumeArguments(SessionInfo session) => string.Empty;
    }
}

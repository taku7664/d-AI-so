using System.Net;
using System.Net.Http.Json;
using Daiso.Host.Tabs.Terminal;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;

namespace Daiso.Host.Tests;

/// <summary>말풍선 보기: 세션 기록을 말풍선 칸으로 읽기, 늘어난 만큼만 읽기, / 목록.</summary>
public sealed class RoomChatTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "daiso-host-tests", "chat-" + Guid.NewGuid().ToString("N"));
    private readonly ClaudeProvider _claude;
    private readonly string _path;

    public RoomChatTests()
    {
        Directory.CreateDirectory(_folder);
        _claude = new ClaudeProvider(new ProviderHome(_folder), new ProcessProbe());
        _path = Path.Combine(_folder, "s.jsonl");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더라 남아도 된다
        }
    }

    private const string Me = """{"type":"user","timestamp":"2026-10-07T00:00:00.000Z","message":{"role":"user","content":"빌드해 줘\n둘째 줄"}}""";
    private const string Ai = """{"type":"assistant","timestamp":"2026-10-07T00:00:01.000Z","message":{"id":"m1","model":"claude-opus-5","content":[{"type":"text","text":"빌드합니다."},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet build"}}]}}""";
    private const string Failed = """{"type":"user","timestamp":"2026-10-07T00:00:02.000Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":"error"}]}}""";
    private const string Noise = """{"type":"user","timestamp":"2026-10-07T00:00:03.000Z","message":{"role":"user","content":"<task-notification>done</task-notification>"}}""";
    private const string Done = """{"type":"assistant","timestamp":"2026-10-07T00:00:04.000Z","message":{"id":"m2","model":"claude-opus-5","content":[{"type":"text","text":"끝났습니다."}]}}""";

    [Fact]
    public async Task A_transcript_becomes_bubbles_tool_calls_and_failed_results()
    {
        File.WriteAllText(_path, string.Join('\n', Me, Ai, Failed, Noise) + "\n");

        var chat = await new RoomChat().ReadAsync(_claude, _path, 0, default);

        chat.Found.Should().BeTrue();
        chat.Items.Select(item => (item.Kind, item.Tool, item.Error)).Should().Equal(
            ("me", (string?)null, false),
            ("ai", null, false),
            ("tool", "Bash", false),
            ("result", null, true));
        chat.Items[0].Text.Should().Be("빌드해 줘\n둘째 줄", because: "여러 줄 메시지는 한 칸이다");
    }

    [Fact]
    public async Task Only_what_was_added_is_sent_and_a_half_written_line_waits()
    {
        var chat = new RoomChat();
        File.WriteAllText(_path, Me + "\n");
        var first = await chat.ReadAsync(_claude, _path, 0, default);

        // 쓰는 중인 줄(줄바꿈 전)은 아직 읽지 않는다
        File.AppendAllText(_path, Done);
        (await chat.ReadAsync(_claude, _path, first.Last, default)).Items.Should().BeEmpty();

        File.AppendAllText(_path, "\n");
        var next = await chat.ReadAsync(_claude, _path, first.Last, default);
        next.Items.Should().ContainSingle().Which.Text.Should().Be("끝났습니다.");
        (await chat.ReadAsync(_claude, _path, next.Last, default)).Items.Should().BeEmpty(because: "같은 줄을 두 번 보내지 않는다");
    }

    [Fact]
    public async Task No_transcript_yet_is_not_an_error()
    {
        var chat = await new RoomChat().ReadAsync(_claude, null, 0, default);

        chat.Found.Should().BeFalse();
        chat.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task The_command_list_has_builtins_and_the_users_skills()
    {
        await using var host = await RunningHost.StartAsync(home: home =>
        {
            var skill = Path.Combine(home, ".claude", "skills", "my-skill");
            Directory.CreateDirectory(skill);
            File.WriteAllText(Path.Combine(skill, "SKILL.md"), "---\nname: my-skill\ndescription: 내 스킬 설명\n---\n본문\n");
        });

        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Get, "/api/terminal/commands?tool=claude");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var groups = (await response.Content.ReadFromJsonAsync<List<CommandGroup>>())!;

        groups.Should().Contain(group => group.Kind == "builtin" && group.Items.Any(item => item.Name == "/login" && item.Terminal));
        groups.Should().Contain(group => group.Kind == "user" && group.Items.Any(item => item.Name == "/my-skill" && item.Description == "내 스킬 설명"));
    }
}

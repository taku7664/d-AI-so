using Daiso.Core.Chat;
using Daiso.Infrastructure.Chat;

namespace Daiso.Infrastructure.Tests;

/// <summary>
/// FEATURE_PLAN B 모드 — 엔진이 실제 프로세스의 stream-json stdout을 읽어 사건으로 올리는지.
/// 진짜 claude 대신 파일 내용을 그대로 내보내는 프로세스(cmd /c type)로 라이브 없이 확인한다.
/// </summary>
public sealed class ClaudeChatSessionTests
{
    [Fact]
    public async Task Reads_stream_json_lines_from_a_process_and_raises_events()
    {
        var dir = Directory.CreateTempSubdirectory("daiso-chat-");
        var file = Path.Combine(dir.FullName, "stream.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            """{"type":"system","subtype":"init","session_id":"s1","model":"claude-x"}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"안"}}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"녕"}}}""",
            """{"type":"result","subtype":"success","result":"끝"}""",
        ]);

        var events = new List<ChatEvent>();
        var gate = new object();
        var exited = new TaskCompletionSource<int>();

        using var session = ClaudeChatSession.Start("cmd.exe", $"/c type \"{file}\"", dir.FullName);
        session.Event += e => { lock (gate) { events.Add(e); } };
        session.Exited += code => exited.TrySetResult(code);

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(200);

        lock (gate)
        {
            events.Should().ContainSingle(e => e is ChatEvent.Started);
            events.OfType<ChatEvent.AssistantDelta>().Select(d => d.Text).Should().Equal("안", "녕");
            events.Should().ContainSingle(e => e is ChatEvent.TurnEnded);
        }

        dir.Delete(recursive: true);
    }

    [Fact]
    public async Task A_bad_working_directory_throws_instead_of_hanging()
    {
        var act = () => ClaudeChatSession.Start("cmd.exe", "/c echo x", @"C:\no\such\daiso\dir");
        await Task.Yield();
        act.Should().Throw<Exception>();
    }
}

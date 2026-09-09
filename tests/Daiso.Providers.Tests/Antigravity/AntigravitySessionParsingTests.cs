using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Antigravity;

namespace Daiso.Providers.Tests.Antigravity;

/// <summary>
/// ARCHITECTURE §4.5 — 기록은 리플레이해야 한다. fixture에는 같은 id 덧쓰기(m2에 토큰이 나중에 붙음),
/// `$rewindTo`(m9 삭제), `$set.messages`(목록 교체 + m4 추가)가 모두 들어 있다.
/// </summary>
public sealed class AntigravitySessionParsingTests
{
    private readonly AntigravityProvider _provider = new(new ProviderHome(Path.GetTempPath()));

    [Fact]
    public async Task Replay_gives_the_final_state_not_the_raw_line_count()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.GeminiPath("session-modern.jsonl"), default);

        info.Tool.Should().Be(ToolKind.Antigravity);
        info.Id.Should().Be("7a1b2c3d-1111-2222-3333-444455556666");
        info.UserMessageCount.Should().Be(2, because: "m1, m4. 되감긴 m9는 빠진다");
        info.AssistantMessageCount.Should().Be(2, because: "m2, m5. m2는 두 번 나오지만 한 건이다");
        info.FirstPrompt.Should().Be("더미 질문 1");
        info.StartedAt.Should().Be(new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_rewound_message_never_reaches_the_stream()
    {
        var messages = await Read(Fixtures.GeminiPath("session-modern.jsonl"));

        messages.Should().NotContain(m => m.Text.Contains("REWOUND", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Content_may_be_a_string_or_a_list_of_text_parts()
    {
        var messages = await Read(Fixtures.GeminiPath("session-modern.jsonl"));

        messages.Where(m => m.Role == MessageRole.User).Select(m => m.Text)
            .Should().Equal("더미 질문 1", "더미 질문 2");
        messages.Where(m => m.Role == MessageRole.Assistant).Select(m => m.Text)
            .Should().Equal("더미 답변 1", "더미 답변 2");
    }

    [Fact]
    public async Task Tool_calls_become_tool_messages_without_their_result_body()
    {
        var messages = await Read(Fixtures.GeminiPath("session-modern.jsonl"));

        var tool = messages.Should().ContainSingle(m => m.Role == MessageRole.Tool).Subject;
        tool.Text.Should().StartWith("read_file").And.Contain("README.md").And.Contain("[success]");
        messages.Should().NotContain(m => m.Text.Contains("RESULT_BODY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Info_lines_are_system_messages()
    {
        var messages = await Read(Fixtures.GeminiPath("session-modern.jsonl"));

        messages.Should().ContainSingle(m => m.Role == MessageRole.System && m.Text == "컨텍스트 요약됨");
    }

    [Fact]
    public async Task Tokens_attached_later_to_the_same_id_count_once_and_split_by_day()
    {
        var days = new List<UsageDay>();

        await foreach (var day in _provider.ReadUsageAsync(Fixtures.GeminiPath("session-modern.jsonl"), 0, new DateOnly(2026, 9, 1), default))
        {
            days.Add(day);
        }

        days.Should().HaveCount(2);
        // 9/1: m2 한 번만 — input 100 + tool 5, output 40 + thoughts 10, cached 20
        days[0].Date.Should().Be(new DateOnly(2026, 9, 1));
        days[0].Usage.Input.Should().Be(105);
        days[0].Usage.Output.Should().Be(50);
        days[0].Usage.CacheRead.Should().Be(20);
        days[0].Usage.Model.Should().Be("gemini-2.5-pro");
        days[1].Usage.Input.Should().Be(200);
    }

    [Fact]
    public async Task Session_info_sums_the_usage_of_the_final_state()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.GeminiPath("session-modern.jsonl"), default);

        info.Usage.Input.Should().Be(305);
        info.Usage.Output.Should().Be(110);
        info.Usage.Model.Should().Be("gemini-2.5-pro");
    }

    [Fact]
    public async Task Reading_from_an_offset_still_replays_the_whole_file()
    {
        // 중간부터 읽으면 $set 교체와 되감기를 놓친다. 오프셋을 받아도 처음부터 읽는다
        var fromStart = await Read(Fixtures.GeminiPath("session-modern.jsonl"));
        var fromMiddle = new List<SessionMessage>();

        await foreach (var message in _provider.ReadMessagesAsync(Fixtures.GeminiPath("session-modern.jsonl"), 500, default))
        {
            fromMiddle.Add(message);
        }

        fromMiddle.Should().Equal(fromStart);
        _provider.AppendOnlySessions.Should().BeFalse();
    }

    [Fact]
    /// <summary>
    /// `agy --help` (1.1.28): 대화를 id 로 이어 여는 것은 `--conversation` 이다.
    /// `--resume` 는 은퇴한 Gemini CLI 의 플래그였다 — 되돌아오면 여기서 걸린다.
    /// </summary>
    public void Resume_uses_the_session_id()
    {
        var session = new SessionInfo(ToolKind.Antigravity, "abc", "x", null, default, default, 0, 0, 0, null, TokenUsage.Zero, null, false, false);

        _provider.BuildResumeArguments(session).Should().Be("--conversation abc");
    }

    private async Task<List<SessionMessage>> Read(string path)
    {
        var list = new List<SessionMessage>();

        await foreach (var message in _provider.ReadMessagesAsync(path, 0, default))
        {
            list.Add(message);
        }

        return list;
    }
}

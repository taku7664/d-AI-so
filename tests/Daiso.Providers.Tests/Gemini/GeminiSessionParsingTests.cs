using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Gemini;

namespace Daiso.Providers.Tests.Gemini;

/// <summary>ARCHITECTURE §4.5 — 헤더 줄 + 메시지 줄 jsonl.</summary>
public sealed class GeminiSessionParsingTests
{
    private readonly GeminiProvider _provider = new(new ProviderHome(Path.GetTempPath()));

    [Fact]
    public async Task Counts_users_and_assistants_and_takes_the_first_prompt()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.GeminiPath("session-modern.jsonl"), default);

        info.Tool.Should().Be(ToolKind.Gemini);
        info.Id.Should().Be("7a1b2c3d-1111-2222-3333-444455556666");
        info.UserMessageCount.Should().Be(2);
        info.AssistantMessageCount.Should().Be(2);
        info.FirstPrompt.Should().Be("더미 질문 1");
        info.StartedAt.Should().Be(new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero));
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
    public async Task Tokens_are_added_per_message_and_split_by_day()
    {
        var days = new List<UsageDay>();

        await foreach (var day in _provider.ReadUsageAsync(Fixtures.GeminiPath("session-modern.jsonl"), 0, new DateOnly(2026, 9, 1), default))
        {
            days.Add(day);
        }

        days.Should().HaveCount(2);
        // 9/1: input 100 + tool 5, output 40 + thoughts 10, cached 20
        days[0].Date.Should().Be(new DateOnly(2026, 9, 1));
        days[0].Usage.Input.Should().Be(105);
        days[0].Usage.Output.Should().Be(50);
        days[0].Usage.CacheRead.Should().Be(20);
        days[0].Usage.Model.Should().Be("gemini-2.5-pro");
        days[1].Usage.Input.Should().Be(200);
    }

    [Fact]
    public async Task Session_info_sums_the_usage_of_the_whole_file()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.GeminiPath("session-modern.jsonl"), default);

        info.Usage.Input.Should().Be(305);
        info.Usage.Output.Should().Be(110);
        info.Usage.Model.Should().Be("gemini-2.5-pro");
    }

    [Fact]
    public void Resume_uses_the_session_id()
    {
        var session = new SessionInfo(ToolKind.Gemini, "abc", "x", null, default, default, 0, 0, 0, null, TokenUsage.Zero, null, false, false);

        _provider.BuildResumeArguments(session).Should().Be("--resume abc");
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

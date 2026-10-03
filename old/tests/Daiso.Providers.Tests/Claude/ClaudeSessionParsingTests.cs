using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Claude;

/// <summary>ARCHITECTURE §4.1 레코드 분류·카운트·usage 규칙.</summary>
public sealed class ClaudeSessionParsingTests
{
    private readonly ClaudeProvider _provider = new(new ProviderHome(Path.GetTempPath()), new FakeProcessProbe());

    [Fact]
    public async Task User_count_excludes_tool_results_meta_and_sidechain()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.ClaudePath("session-basic.jsonl"), default);

        // 문자열 user 1건 + text 배열 user 1건. tool_result·isMeta·isSidechain은 빠진다.
        info.UserMessageCount.Should().Be(2);
    }

    [Fact]
    public async Task Assistant_count_excludes_sidechain_and_tool_only_records()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.ClaudePath("session-basic.jsonl"), default);

        // 텍스트가 있는 어시스턴트 2건 (일반 1건 + synthetic 1건). tool_use 전용·sidechain은 빠진다.
        info.AssistantMessageCount.Should().Be(2);
    }

    [Fact]
    public async Task First_prompt_is_the_first_user_text()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.ClaudePath("session-basic.jsonl"), default);

        info.FirstPrompt.Should().Be("더미 질문 1");
    }

    [Fact]
    public async Task Usage_skips_the_synthetic_model()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.ClaudePath("session-basic.jsonl"), default);

        // 일반 어시스턴트 2건(100+10, 20+2, 5+0, 50+1) + sidechain 1건(7, 3, 0, 0).
        // synthetic 999들은 어디에도 더해지지 않는다.
        info.Usage.Should().Be(new TokenUsage(117, 25, 5, 51, "claude-opus-5"));
    }

    [Fact]
    public async Task Metadata_comes_from_the_first_records()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.ClaudePath("session-basic.jsonl"), default);

        info.Tool.Should().Be(ToolKind.Claude);
        info.Id.Should().Be(Fixtures.ClaudeSessionId);
        info.ProjectPath.Should().Be(@"C:\Fixture\Project");
        info.StartedAt.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        info.ToolVersion.Should().Be("2.0.0");
        info.IsArchived.Should().BeFalse();
        info.SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Messages_are_classified_in_file_order()
    {
        var messages = await Read(Fixtures.ClaudePath("session-basic.jsonl"), 0);

        messages.Select(m => m.Role).Should().Equal(
            MessageRole.User,        // 문자열 content
            MessageRole.Assistant,   // text 블록
            MessageRole.Tool,        // tool_use 블록
            MessageRole.Tool,        // tool_result 포함 user
            MessageRole.User,        // text 배열
            MessageRole.User,        // sidechain user
            MessageRole.Assistant,   // sidechain assistant
            MessageRole.Assistant,   // synthetic 모델도 메시지로는 남는다
            MessageRole.System);
    }

    [Fact]
    public async Task Sidechain_messages_stay_in_the_stream_but_are_marked()
    {
        var messages = await Read(Fixtures.ClaudePath("session-basic.jsonl"), 0);

        messages.Where(m => m.IsSidechain).Select(m => m.Text)
            .Should().Equal("더미 질문 3", "더미 응답 2");
    }

    [Fact]
    public async Task A_meta_record_produces_no_message()
    {
        var messages = await Read(Fixtures.ClaudePath("session-basic.jsonl"), 0);

        messages.Should().NotContain(m => m.Text.Contains("메타 주입", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_broken_line_is_skipped_without_failing()
    {
        var messages = await Read(Fixtures.ClaudePath("session-basic.jsonl"), 0);

        messages.Should().HaveCount(9);
    }

    [Fact]
    public async Task Reading_from_a_line_boundary_offset_continues_correctly()
    {
        var path = Fixtures.ClaudePath("session-offsets.jsonl");
        var all = await Read(path, 0);
        all.Select(m => m.Text).Should().Equal("더미 질문 1", "더미 응답 1", "더미 질문 2", "더미 응답 2");

        var offset = OffsetAfterLines(path, 2);
        var tail = await Read(path, offset);

        tail.Select(m => m.Text).Should().Equal("더미 질문 2", "더미 응답 2");
    }

    [Fact]
    public async Task An_offset_at_the_end_of_the_file_yields_nothing()
    {
        var path = Fixtures.ClaudePath("session-offsets.jsonl");

        var tail = await Read(path, new FileInfo(path).Length);

        tail.Should().BeEmpty();
    }

    [Fact]
    public void Resume_arguments_use_the_session_id()
    {
        var session = new SessionInfo(
            ToolKind.Claude, "abc", "p", null, default, default, 0, 0, 0, null,
            TokenUsage.Zero, null, false, false);

        _provider.BuildResumeArguments(session).Should().Be("--resume abc");
    }

    private static async Task<List<SessionMessage>> Read(string path, long offset)
    {
        var messages = new List<SessionMessage>();

        await foreach (var message in new ClaudeProvider(
            new ProviderHome(Path.GetTempPath()), new FakeProcessProbe())
            .ReadMessagesAsync(path, offset, default))
        {
            messages.Add(message);
        }

        return messages;
    }

    /// <summary>앞에서 <paramref name="lines"/>줄을 지난 바이트 위치. jsonl은 LF로 저장한다.</summary>
    private static long OffsetAfterLines(string path, int lines)
    {
        var bytes = File.ReadAllBytes(path);
        var seen = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
            {
                continue;
            }

            if (++seen == lines)
            {
                return i + 1;
            }
        }

        return bytes.Length;
    }
}

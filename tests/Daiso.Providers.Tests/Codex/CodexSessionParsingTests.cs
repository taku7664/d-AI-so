using Daiso.Core;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Codex;

/// <summary>ARCHITECTURE §4.2 — 구형·신형 두 형식을 모두 인식해야 한다.</summary>
public sealed class CodexSessionParsingTests
{
    private readonly CodexProvider _provider = new(new ProviderHome(Path.GetTempPath()));

    [Theory]
    [InlineData("rollout-legacy.jsonl")]
    [InlineData("rollout-modern.jsonl")]
    public async Task Both_formats_count_user_messages(string fixture)
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.CodexPath(fixture), default);

        info.UserMessageCount.Should().Be(2);
        info.FirstPrompt.Should().Be("더미 질문 1");
    }

    [Theory]
    [InlineData("rollout-legacy.jsonl")]
    [InlineData("rollout-modern.jsonl")]
    public async Task A_response_item_duplicate_is_not_counted_twice(string fixture)
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.CodexPath(fixture), default);

        info.AssistantMessageCount.Should().Be(1);
    }

    [Theory]
    [InlineData("rollout-legacy.jsonl")]
    [InlineData("rollout-modern.jsonl")]
    public async Task World_state_text_never_reaches_the_message_stream(string fixture)
    {
        var messages = await Read(Fixtures.CodexPath(fixture));

        messages.Should().NotContain(m => m.Text.Contains("AGENTS 전문", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_legacy_format_reads_meta_and_the_last_non_null_token_count()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.CodexPath("rollout-legacy.jsonl"), default);

        info.Tool.Should().Be(ToolKind.Codex);
        info.Id.Should().Be(Fixtures.CodexLegacySessionId);
        info.ProjectPath.Should().Be(@"C:\Fixture\Project");
        info.ToolVersion.Should().Be("0.147.0");

        // 누적값이므로 마지막 레코드 값만 쓴다. info가 null인 레코드는 건너뛴다.
        info.Usage.Should().Be(new TokenUsage(200, 40, 10, 60, "gpt-5-codex"));
    }

    [Fact]
    public async Task The_modern_format_reads_meta_and_the_model_from_thread_settings()
    {
        var info = await _provider.ReadSessionInfoAsync(Fixtures.CodexPath("rollout-modern.jsonl"), default);

        info.Id.Should().Be(Fixtures.CodexModernSessionId);
        info.ToolVersion.Should().Be("0.153.0");
        info.Usage.Should().Be(new TokenUsage(300, 60, 15, 90, "gpt-5.1-codex"));
    }

    [Fact]
    public async Task Developer_messages_are_classified_as_System()
    {
        var messages = await Read(Fixtures.CodexPath("rollout-legacy.jsonl"));

        messages.Where(m => m.Role == MessageRole.System).Select(m => m.Text)
            .Should().Contain("더미 개발자 지시문");
    }

    [Fact]
    public async Task A_response_item_user_role_is_a_system_injection_not_a_prompt()
    {
        var path = Fixtures.CodexPath("rollout-legacy.jsonl");

        var messages = await Read(path);
        var info = await _provider.ReadSessionInfoAsync(path, default);

        // 사람 입력은 event_msg.user_message로만 온다. 주입된 목록은 카운트에 들어가면 안 된다.
        messages.Should().Contain(m =>
            m.Role == MessageRole.System && m.Text == "더미 시스템 주입 목록");
        messages.Should().NotContain(m =>
            m.Role == MessageRole.User && m.Text == "더미 시스템 주입 목록");
        info.UserMessageCount.Should().Be(2);
    }

    [Fact]
    public async Task Reasoning_and_command_records_produce_no_messages()
    {
        var legacy = await Read(Fixtures.CodexPath("rollout-legacy.jsonl"));
        var modern = await Read(Fixtures.CodexPath("rollout-modern.jsonl"));

        legacy.Should().NotContain(m => m.Text.Contains("더미 추론", StringComparison.Ordinal));
        modern.Should().NotContain(m => m.Text.Contains("Get-Date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Messages_keep_file_order()
    {
        var messages = await Read(Fixtures.CodexPath("rollout-modern.jsonl"));

        messages.Select(m => (m.Role, m.Text)).Should().Equal(
            (MessageRole.User, "더미 질문 1"),
            (MessageRole.Assistant, "더미 응답 1"),
            (MessageRole.User, "더미 질문 2"));
    }

    [Fact]
    public async Task Reading_from_a_line_boundary_offset_continues_correctly()
    {
        var path = Fixtures.CodexPath("rollout-modern.jsonl");
        var offset = OffsetAfterLines(path, 5);

        var tail = await Read(path, offset);

        tail.Select(m => m.Text).Should().Equal("더미 응답 1", "더미 질문 2");
    }

    [Fact]
    public void Resume_arguments_use_the_bare_subcommand()
    {
        var session = new SessionInfo(
            ToolKind.Codex, "abc", "p", null, default, default, 0, 0, 0, null,
            TokenUsage.Zero, null, false, false);

        _provider.BuildResumeArguments(session).Should().Be("resume abc");
    }

    [Fact]
    public void The_executable_is_the_npm_shell_name()
    {
        _provider.ExecutableName.Should().Be("codex");
        _provider.RulesFileName.Should().Be("AGENTS.md");
    }

    /// <summary>
    /// 같은 어시스턴트 말이 <b>떨어져서</b> 두 번 나와도 한 번만 센다.
    /// 바로 앞 하나만 기억하던 때는 A B A 가 다 통과해 목록·검색에 두 번 보였다 (2026-09-11 점검).
    /// </summary>
    [Fact]
    public async Task The_same_answer_far_apart_is_still_one_message()
    {
        var path = Path.Combine(Fixtures.CreateTempDirectory(), "rollout-dup.jsonl");

        await File.WriteAllLinesAsync(
            path,
            [
                """{"type":"session_meta","timestamp":"2026-09-01T00:00:00Z","payload":{"id":"dup","cwd":"C:/X","cli_version":"0.153.0"}}""",
                """{"type":"event_msg","timestamp":"2026-09-01T00:00:01Z","payload":{"type":"agent_message","message":"같은 답"}}""",
                """{"type":"event_msg","timestamp":"2026-09-01T00:00:02Z","payload":{"type":"agent_message","message":"다른 답"}}""",
                """{"type":"event_msg","timestamp":"2026-09-01T00:00:03Z","payload":{"type":"agent_message","message":"같은 답"}}""",
            ]);

        var messages = await Read(path);

        messages.Where(message => message.Text == "같은 답").Should().HaveCount(1);
        messages.Where(message => message.Text == "다른 답").Should().HaveCount(1);
    }

    /// <summary>
    /// 바깥 에이전트의 도구 호출은 <b>대화가 아니다</b>. Codex 가 그것을 어시스턴트 본문으로 적어 두는데,
    /// 그대로 두면 검색이 파일 읽기 기록으로 덮인다 — 실제 인덱스의 27%가 이것이었다 (2026-09-11 실측).
    /// </summary>
    [Fact]
    public async Task An_external_agent_tool_call_is_a_tool_message()
    {
        var path = Path.Combine(Fixtures.CreateTempDirectory(), "rollout-tool.jsonl");

        await File.WriteAllLinesAsync(
            path,
            [
                """{"type":"session_meta","timestamp":"2026-09-01T00:00:00Z","payload":{"id":"tool","cwd":"C:/X","cli_version":"0.153.0"}}""",
                """{"type":"event_msg","timestamp":"2026-09-01T00:00:01Z","payload":{"type":"agent_message","message":"[external_agent_tool_call: Read] file: c:/x/y.cs"}}""",
                """{"type":"event_msg","timestamp":"2026-09-01T00:00:02Z","payload":{"type":"agent_message","message":"사람에게 하는 말"}}""",
            ]);

        var messages = await Read(path);

        messages.Should().ContainSingle(message => message.Role == MessageRole.Assistant)
            .Which.Text.Should().Be("사람에게 하는 말");
        messages.Should().ContainSingle(message => message.Role == MessageRole.Tool);
    }

    private async Task<List<SessionMessage>> Read(string path, long offset = 0)
    {
        var messages = new List<SessionMessage>();

        await foreach (var message in _provider.ReadMessagesAsync(path, offset, default))
        {
            messages.Add(message);
        }

        return messages;
    }

    private static long OffsetAfterLines(string path, int lines)
    {
        var bytes = File.ReadAllBytes(path);
        var seen = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n' && ++seen == lines)
            {
                return i + 1;
            }
        }

        return bytes.Length;
    }
}

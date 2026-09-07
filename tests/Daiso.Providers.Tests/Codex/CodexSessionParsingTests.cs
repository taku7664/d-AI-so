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

        messages.Should().ContainSingle(m => m.Role == MessageRole.System)
            .Which.Text.Should().Be("더미 개발자 지시문");
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

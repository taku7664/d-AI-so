using Daiso.Core;
using Daiso.Providers.Claude;

namespace Daiso.Providers.Tests.Claude;

/// <summary>임시 홈 폴더로 세션 열거·활성 판정·컨텍스트 목록을 확인한다.</summary>
public sealed class ClaudeProviderTests : IDisposable
{
    private const int ActivePid = 424242;

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

    [Fact]
    public async Task Sessions_under_project_folders_are_listed_and_nested_folders_ignored()
    {
        var provider = new ClaudeProvider(Fixtures.CreateClaudeHome(_home), new FakeProcessProbe());

        var sessions = await Enumerate(provider);

        sessions.Should().HaveCount(2);
        sessions.Select(s => Path.GetFileName(s.FilePath))
            .Should().BeEquivalentTo(["session-basic.jsonl", "session-offsets.jsonl"]);
    }

    [Fact]
    public async Task Enumeration_fills_metadata_without_scanning_the_body()
    {
        var provider = new ClaudeProvider(Fixtures.CreateClaudeHome(_home), new FakeProcessProbe());

        var session = (await Enumerate(provider))
            .Single(s => Path.GetFileName(s.FilePath) == "session-basic.jsonl");

        session.Id.Should().Be(Fixtures.ClaudeSessionId);
        session.ProjectPath.Should().Be(@"C:\Fixture\Project");
        session.ToolVersion.Should().Be("2.0.0");

        // 본문을 세지 않으므로 카운트는 0이다.
        session.UserMessageCount.Should().Be(0);
        session.Usage.Should().Be(TokenUsage.Zero);
    }

    [Fact]
    public async Task A_session_owned_by_a_live_process_is_active()
    {
        var provider = new ClaudeProvider(
            Fixtures.CreateClaudeHome(_home, ActivePid),
            new FakeProcessProbe(ActivePid));

        var sessions = await Enumerate(provider);

        sessions.Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task A_session_whose_process_is_gone_is_not_active()
    {
        var provider = new ClaudeProvider(
            Fixtures.CreateClaudeHome(_home, ActivePid),
            new FakeProcessProbe());

        var sessions = await Enumerate(provider);

        sessions.Should().OnlyContain(s => !s.IsActive);
    }

    [Fact]
    public async Task Auth_reads_both_files_from_the_home_folder()
    {
        var provider = new ClaudeProvider(Fixtures.CreateClaudeHome(_home), new FakeProcessProbe());

        var status = await provider.GetAuthStatusAsync(default);

        status.Tool.Should().Be(ToolKind.Claude);
        status.Email.Should().Be("fixture@example.test");
        status.SessionExpiresAt.Should().Be(Fixtures.Expiry);
    }

    [Fact]
    public async Task Auth_without_any_file_is_Missing()
    {
        var provider = new ClaudeProvider(new Providers.Common.ProviderHome(_home), new FakeProcessProbe());

        var status = await provider.GetAuthStatusAsync(default);

        status.State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public async Task An_empty_home_yields_no_sessions()
    {
        var provider = new ClaudeProvider(new Providers.Common.ProviderHome(_home), new FakeProcessProbe());

        (await Enumerate(provider)).Should().BeEmpty();
    }

    [Fact]
    public void Context_file_patterns_follow_the_documented_load_order()
    {
        var provider = new ClaudeProvider(new Providers.Common.ProviderHome(_home), new FakeProcessProbe());

        var patterns = provider.ContextFilePatterns(@"C:\Fixture\Project");

        patterns[0].Should().Be(Path.Combine(_home, ".claude", "CLAUDE.md"));
        patterns.Should().ContainInOrder(
            @"C:\CLAUDE.md",
            @"C:\Fixture\CLAUDE.md",
            @"C:\Fixture\Project\CLAUDE.md",
            @"C:\Fixture\Project\.claude\CLAUDE.md",
            @"C:\Fixture\Project\CLAUDE.local.md",
            @"C:\Fixture\Project\.claude\rules\*.md",
            @"C:\Fixture\Project\PROJECT_RULES.daiso");
        patterns[^1].Should().EndWith("PROJECT_RULES.daiso");
    }

    [Fact]
    public void The_executable_is_the_npm_shell_name()
    {
        var provider = new ClaudeProvider(new Providers.Common.ProviderHome(_home), new FakeProcessProbe());

        provider.ExecutableName.Should().Be("claude");
        provider.RulesFileName.Should().Be("CLAUDE.md");
    }

    private static async Task<List<SessionInfo>> Enumerate(ClaudeProvider provider)
    {
        var sessions = new List<SessionInfo>();

        await foreach (var session in provider.EnumerateSessionsAsync(default))
        {
            sessions.Add(session);
        }

        return sessions;
    }
}

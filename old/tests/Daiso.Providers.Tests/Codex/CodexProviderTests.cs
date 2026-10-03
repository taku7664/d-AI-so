using Daiso.Core;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Codex;

/// <summary>임시 홈 폴더로 세션 열거와 아카이브 판정을 확인한다.</summary>
public sealed class CodexProviderTests : IDisposable
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

    [Fact]
    public async Task Sessions_and_archived_sessions_are_both_listed()
    {
        var provider = new CodexProvider(Fixtures.CreateCodexHome(_home));

        var sessions = await Enumerate(provider);

        sessions.Should().HaveCount(3);
        sessions.Count(s => s.IsArchived).Should().Be(1);
        sessions.Count(s => !s.IsArchived).Should().Be(2);
    }

    [Fact]
    public async Task Enumeration_reads_meta_from_the_session_meta_line()
    {
        var provider = new CodexProvider(Fixtures.CreateCodexHome(_home));

        var session = (await Enumerate(provider))
            .First(s => !s.IsArchived && s.ToolVersion == "0.153.0");

        session.Id.Should().Be(Fixtures.CodexModernSessionId);
        session.ProjectPath.Should().Be(@"C:\Fixture\Project");
        session.UserMessageCount.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_home_yields_no_sessions()
    {
        var provider = new CodexProvider(new ProviderHome(_home));

        (await Enumerate(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Auth_reads_the_home_auth_file()
    {
        var provider = new CodexProvider(Fixtures.CreateCodexHome(_home));

        var status = await provider.GetAuthStatusAsync(default);

        status.Tool.Should().Be(ToolKind.Codex);
        status.SessionExpiresAt.Should().Be(Fixtures.Expiry);
    }

    [Fact]
    public void Context_file_patterns_start_at_the_global_agents_file()
    {
        var provider = new CodexProvider(new ProviderHome(_home));

        var patterns = provider.ContextFilePatterns(_home);

        patterns[0].Should().Be(Path.Combine(_home, ".codex", "AGENTS.md"));
        patterns[^1].Should().Be(Path.Combine(_home, "PROJECT_RULES.daiso"));
        patterns.Should().Contain(Path.Combine(_home, "AGENTS.md"));
    }

    [Fact]
    public void Context_file_patterns_walk_down_from_the_git_root()
    {
        var root = Path.Combine(_home, "repo");
        var nested = Path.Combine(root, "src", "app");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(nested);

        var patterns = new CodexProvider(new ProviderHome(_home)).ContextFilePatterns(nested);

        patterns.Should().ContainInOrder(
            Path.Combine(root, "AGENTS.md"),
            Path.Combine(root, "src", "AGENTS.md"),
            Path.Combine(nested, "AGENTS.md"));
    }

    private static async Task<List<SessionInfo>> Enumerate(CodexProvider provider)
    {
        var sessions = new List<SessionInfo>();

        await foreach (var session in provider.EnumerateSessionsAsync(default))
        {
            sessions.Add(session);
        }

        return sessions;
    }
}

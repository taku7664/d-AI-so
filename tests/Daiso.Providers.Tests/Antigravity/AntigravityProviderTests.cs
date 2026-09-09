using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Antigravity;

namespace Daiso.Providers.Tests.Antigravity;

/// <summary>임시 홈 폴더로 세션 열거·프로젝트 되짚기·서버 세션 제외를 확인한다.</summary>
public sealed class AntigravityProviderTests : IDisposable
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
    public async Task Named_and_hashed_project_folders_are_both_listed_and_server_sessions_are_skipped()
    {
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home));

        var sessions = await Enumerate(provider);

        // 이름 폴더 1 + 해시 폴더 1. a2a-server는 빠진다
        sessions.Should().HaveCount(2);
        sessions.Should().OnlyContain(s => s.Tool == ToolKind.Antigravity);
        sessions.Should().NotContain(s => s.Id == "a2a-server");
    }

    [Fact]
    public async Task The_project_path_comes_back_from_projects_json_by_name_or_hash()
    {
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home));

        var sessions = await Enumerate(provider);

        sessions.Should().OnlyContain(s => s.ProjectPath == @"C:\fixture\project");
    }

    [Fact]
    public async Task Enumeration_reads_only_the_header()
    {
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home));

        var session = (await Enumerate(provider)).First();

        session.Id.Should().Be("7a1b2c3d-1111-2222-3333-444455556666");
        session.UserMessageCount.Should().Be(0);
        session.StartedAt.Should().Be(new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task An_empty_home_yields_no_sessions()
    {
        var provider = new AntigravityProvider(new ProviderHome(_home));

        (await Enumerate(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task Auth_reads_both_home_files()
    {
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home));

        var status = await provider.GetAuthStatusAsync(default);

        status.State.Should().Be(AuthState.LoggedIn);
        status.Email.Should().Be("fixture@example.com");
    }

    [Fact]
    public void Context_files_start_at_the_global_GEMINI_md_and_end_with_the_preset()
    {
        var provider = new AntigravityProvider(new ProviderHome(_home));
        var project = Path.Combine(_home, "proj");
        Directory.CreateDirectory(Path.Combine(project, ".git"));

        var patterns = provider.ContextFilePatterns(project);

        patterns.First().Should().Be(Path.Combine(_home, ".gemini", "GEMINI.md"));
        patterns.Should().Contain(Path.Combine(project, "GEMINI.md"));
        patterns.Last().Should().EndWith("PROJECT_RULES.daiso");
    }

    private static async Task<List<SessionInfo>> Enumerate(AntigravityProvider provider)
    {
        var list = new List<SessionInfo>();

        await foreach (var session in provider.EnumerateSessionsAsync(default))
        {
            list.Add(session);
        }

        return list;
    }
}

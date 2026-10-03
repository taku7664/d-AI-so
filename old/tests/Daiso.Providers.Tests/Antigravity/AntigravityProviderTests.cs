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

    /// <summary>
    /// 로그인 판정은 자격 증명 관리자 항목 하나로만 한다. fixture 홈에는 은퇴한 Gemini CLI 의 로그인 파일이 들어 있는데,
    /// 그것이 있어도 자격 증명이 없으면 로그인이 아니다 — 전에는 그 파일 때문에 로그인으로 보였다.
    /// </summary>
    [Fact]
    public async Task A_leftover_gemini_login_file_does_not_make_antigravity_logged_in()
    {
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home), new FakeCredentialProbe(exists: false));

        var status = await provider.GetAuthStatusAsync(default);

        status.State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public async Task The_credential_manager_entry_decides_that_it_is_logged_in()
    {
        var probe = new FakeCredentialProbe(exists: true);
        var provider = new AntigravityProvider(Fixtures.CreateGeminiHome(_home), probe);

        var status = await provider.GetAuthStatusAsync(default);

        status.State.Should().Be(AuthState.LoggedIn);
        status.Email.Should().BeNull(because: "은퇴한 도구의 계정 파일을 빌려 쓰지 않는다");
        probe.AskedFor.Should().Be(AntigravityAuthReader.CredentialTarget);
    }

    /// <summary>자격 증명은 파일이 아니라 로그인 프로필(§5.7)로 옮길 수 없다. 필수 파일이 없어야 프로필이 "옮겼다"고 착각하지 않는다.</summary>
    [Fact]
    public void No_auth_file_is_required()
    {
        new AntigravityProvider(new ProviderHome(_home)).AuthFiles.Should().NotContain(file => file.Required);
    }

    private sealed class FakeCredentialProbe : ICredentialProbe
    {
        private readonly bool _exists;

        public FakeCredentialProbe(bool exists) => _exists = exists;

        public string? AskedFor { get; private set; }

        public bool Exists(string target)
        {
            AskedFor = target;
            return _exists;
        }
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

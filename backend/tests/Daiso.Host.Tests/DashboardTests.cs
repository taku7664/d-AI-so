using System.Net;
using System.Net.Http.Json;
using Daiso.Host.Services;
using Daiso.Host.Shared;
using Daiso.Host.Tabs.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>요약 탭 <c>/api/dashboard</c> 와 위 줄 계정 단추 <c>/api/accounts</c>. 옛 시험의 Claude fixture 를 임시 홈에 놓는다.</summary>
public sealed class DashboardTests : IAsyncLifetime
{
    private const string Project = @"C:\Fixture\Project";

    private RunningHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await RunningHost.StartAsync();

        var claude = Path.Combine(_host.Home, ".claude");
        var folder = Path.Combine(claude, "projects", "C--Fixture-Project");
        Directory.CreateDirectory(folder);
        File.Copy(Fixture("session-basic.jsonl"), Path.Combine(folder, "session-basic.jsonl"));
        // 세션의 마지막 수정 시각은 파일 시각이다. 복사하면 지금이 되므로 오래된 세션으로 되돌린다
        File.SetLastWriteTimeUtc(Path.Combine(folder, "session-basic.jsonl"), new DateTime(2026, 9, 1, 0, 2, 0, DateTimeKind.Utc));
        File.Copy(Fixture("credentials.json"), Path.Combine(claude, ".credentials.json"));

        var index = _host.App.Services.GetRequiredService<IndexService>();
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));
        index.TryStart(rebuild: false);
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name);

    [Fact]
    public async Task A_project_home_has_recent_sessions_with_the_last_prompt()
    {
        var home = await GetAsync<DashboardResponse>("/api/dashboard?project=" + Uri.EscapeDataString(Project));

        home.Project.Should().Be(Project);
        var recent = home.Recent.Should().ContainSingle().Subject;
        recent.Session.Title.Should().Be("더미 질문 1");
        recent.LastPrompt.Should().NotBeNullOrEmpty().And.NotBe("더미 메타 주입", because: "메타 주입 줄은 사람이 친 글이 아니다");
        home.Week.Should().HaveCount(7);
        home.Today.Sessions.Should().Be(0, because: "fixture 세션은 2026-09-01 것이다");
    }

    [Fact]
    public async Task All_projects_counts_every_session_and_the_bell_has_things_to_fix()
    {
        var all = await GetAsync<DashboardResponse>("/api/dashboard");
        var bell = await GetAsync<BellResponse>("/api/bell");

        all.Project.Should().BeNull();
        all.Recent.Should().ContainSingle();
        bell.Attention.Should().Contain(item => item.Kind == "cleanup" && item.Count == 1, because: "fixture 세션은 30일이 넘었다");
        bell.Last!.ProjectPath.Should().Be(Project);
        bell.Last.Work.LastPrompt.Should().NotBeNullOrEmpty();
        bell.Others.Should().BeEmpty(because: "프로젝트가 하나뿐이다");
    }

    [Fact]
    public async Task Worktrees_answer_even_when_no_project_is_a_git_repository()
    {
        var worktrees = await GetAsync<WorktreesResponse>("/api/dashboard/worktrees");

        worktrees.Worktrees.Should().BeEmpty();
    }

    [Fact]
    public async Task Accounts_show_the_login_without_any_token()
    {
        using var response = await GetRawAsync("/api/accounts");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("DUMMY_TOKEN", because: "토큰 값은 어떤 응답에도 나가지 않는다");
        var claude = (await response.Content.ReadFromJsonAsync<List<Account>>())!.Single(account => account.Tool == "claude");
        claude.State.Should().NotBe("missing");
        claude.Plan.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Profiles_are_saved_under_the_hosts_data_folder_and_can_be_removed()
    {
        (await PostAsync("/api/accounts/claude/profiles", new ProfileRequest(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync("/api/accounts/claude/profiles", new ProfileRequest("회사"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Directory.Exists(Path.Combine(_host.Options.DataDirectory, "profiles")).Should().BeTrue(because: "시험이 진짜 %LOCALAPPDATA% 의 저장 계정을 건드리면 안 된다");
        (await ClaudeAsync()).Profiles.Should().ContainSingle(profile => profile.Name == "회사");

        (await PostAsync("/api/accounts/claude/profiles/remove", new ProfileRequest("회사"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ClaudeAsync()).Profiles.Should().BeEmpty();
        (await PostAsync("/api/accounts/nope/login", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Account> ClaudeAsync() => (await GetAsync<List<Account>>("/api/accounts")).Single(account => account.Tool == "claude");

    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await GetRawAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> GetRawAsync(string path)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, path);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Post, path, _host.Url);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}

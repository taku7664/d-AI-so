using System.Net;
using System.Net.Http.Json;
using Daiso.Host.Services;
using Daiso.Host.Tabs.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>세션 탭 <c>/api/sessions</c>. 옛 시험의 Claude 세션 fixture 하나를 임시 홈에 놓고 돈다.</summary>
public sealed class SessionsTests : IAsyncLifetime
{
    private RunningHost _host = null!;
    private string _session = null!;

    public async Task InitializeAsync()
    {
        _host = await RunningHost.StartAsync();

        var project = Path.Combine(_host.Home, ".claude", "projects", "C--Fixture-Project");
        Directory.CreateDirectory(project);
        _session = Path.Combine(project, "session-basic.jsonl");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "session-basic.jsonl"), _session);

        // 뜰 때 돈 갱신이 끝난 뒤 한 번 더 돌려 방금 놓은 파일을 읽게 한다
        var index = _host.App.Services.GetRequiredService<IndexService>();
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));
        index.TryStart(rebuild: false);
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task The_list_shows_the_session_with_its_first_question_as_title()
    {
        var row = (await ListAsync()).Sessions.Should().ContainSingle().Subject;

        row.Path.Should().BeEquivalentTo(_session);
        row.Tool.Should().Be("claude");
        row.Title.Should().Be("더미 질문 1");
        row.Named.Should().BeFalse();
        row.Orphan.Should().BeTrue(because: "fixture 의 프로젝트 폴더 C:\\Fixture\\Project 는 없다");
        (await ListAsync()).Cleanup.Should().Be(new CleanupRule(30, 20));
    }

    [Fact]
    public async Task Filters_narrow_the_list()
    {
        (await ListAsync("?tool=codex")).Sessions.Should().BeEmpty();
        (await ListAsync("?project=C%3A%5CFixture%5CProject")).Sessions.Should().ContainSingle();
        (await ListAsync("?project=C%3A%5Celsewhere")).Sessions.Should().BeEmpty();
        (await ListAsync("?minMegabytes=1")).Sessions.Should().BeEmpty();
        (await ListAsync("?orphans=true")).Sessions.Should().ContainSingle();
    }

    [Fact]
    public async Task A_name_replaces_the_title_until_it_is_removed()
    {
        (await SendAsync(HttpMethod.Put, "/api/sessions/name", new RenameRequest(_session, "  Stage 1 보안  "))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = (await ListAsync()).Sessions.Single();
        row.Title.Should().Be("Stage 1 보안");
        row.Named.Should().BeTrue();

        await SendAsync(HttpMethod.Put, "/api/sessions/name", new RenameRequest(_session, ""));
        (await ListAsync()).Sessions.Single().Named.Should().BeFalse();
    }

    [Fact]
    public async Task Messages_hide_tool_calls_unless_asked()
    {
        var plain = await GetJsonAsync<MessagesResponse>($"/api/sessions/messages?path={Uri.EscapeDataString(_session)}");
        var withTools = await GetJsonAsync<MessagesResponse>($"/api/sessions/messages?path={Uri.EscapeDataString(_session)}&tools=true");

        plain.Messages.Should().NotBeEmpty().And.OnlyContain(message => message.Role == "user" || message.Role == "assistant");
        plain.Messages[0].Text.Should().Be("더미 질문 1");
        withTools.Messages.Count.Should().BeGreaterThanOrEqualTo(plain.Messages.Count);
    }

    [Fact]
    public async Task Search_needs_two_letters_and_groups_by_session()
    {
        (await GetAsync("/api/sessions/search?q=a")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await GetJsonAsync<SearchResponse>("/api/sessions/search?q=" + Uri.EscapeDataString("더미"));
        result.Groups.Should().ContainSingle().Which.Matches.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_downloads_markdown_named_after_the_tool_and_id()
    {
        using var response = await GetAsync($"/api/sessions/export?path={Uri.EscapeDataString(_session)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("claude-11111111-2222-3333-4444-555555555555.md");
        (await response.Content.ReadAsStringAsync()).Should().Contain("더미 질문 1");
    }

    [Theory]
    [InlineData("/api/sessions/messages?path=")]
    [InlineData("/api/sessions/export?path=")]
    public async Task Files_outside_the_index_are_never_read(string route)
    {
        var secret = Path.Combine(_host.Home, "secret.jsonl");
        File.WriteAllText(secret, "{}");

        using var response = await GetAsync(route + Uri.EscapeDataString(secret));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, because: "인덱스에 없는 경로는 화면이 보내도 읽지 않는다");
    }

    [Fact]
    public async Task Deleting_skips_unknown_paths_and_removes_the_rest()
    {
        var outside = Path.Combine(_host.Home, "not-a-session.jsonl");
        File.WriteAllText(outside, "{}");

        using var response = await SendAsync(HttpMethod.Post, "/api/sessions/delete", new DeleteRequest([_session, outside], Permanent: true));
        var result = (await response.Content.ReadFromJsonAsync<DeleteResponse>())!;

        result.Deleted.Should().ContainSingle().Which.Should().BeEquivalentTo(_session);
        result.Skipped.Should().ContainSingle(skip => skip.Path == outside && skip.Reason.Contains("목록에 없는", StringComparison.Ordinal));
        File.Exists(outside).Should().BeTrue(because: "인덱스에 없는 파일은 지우지 않는다");
        File.Exists(_session).Should().BeFalse();

        await _host.App.Services.GetRequiredService<IndexService>().Current.WaitAsync(TimeSpan.FromSeconds(30));
        (await ListAsync()).Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_rule_must_be_positive_and_is_kept()
    {
        (await SendAsync(HttpMethod.Put, "/api/sessions/cleanup", new CleanupRule(0, 20))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, "/api/sessions/cleanup", new CleanupRule(14, 50))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListAsync()).Cleanup.Should().Be(new CleanupRule(14, 50));
    }

    private Task<SessionsResponse> ListAsync(string query = "") => GetJsonAsync<SessionsResponse>("/api/sessions" + query);

    private async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, path);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string path, T body)
    {
        using var client = _host.Client();
        using var request = _host.Authed(method, path, _host.Url);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}

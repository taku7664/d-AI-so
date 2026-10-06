using System.Net;
using System.Net.Http.Json;
using Daiso.Host.Services;
using Daiso.Host.Shared;

namespace Daiso.Host.Tests;

/// <summary>공용 경로 <c>/api/projects</c> (docs/ARCHITECTURE.md "탭에 속하지 않는 공용 경로").</summary>
public sealed class ProjectsTests : IAsyncLifetime
{
    private RunningHost _host = null!;
    private string _project = null!;

    public async Task InitializeAsync()
    {
        _project = Path.Combine(Path.GetTempPath(), "daiso-host-tests", "project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_project);
        _host = await RunningHost.StartAsync(settings: settings => settings.RecentFolders.Add(_project));
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_project);
    }

    [Fact]
    public async Task Recent_folders_are_projects_even_without_sessions()
    {
        var response = await GetAsync();

        response.Current.Should().BeNull(because: "처음에는 모든 프로젝트다");
        response.Projects.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ProjectItem(Path.GetFileName(_project), _project, 0, null, true));
    }

    [Fact]
    public async Task Choosing_a_project_is_kept_in_the_settings_file()
    {
        using (var put = await PutAsync(_project))
        {
            put.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await GetAsync()).Current.Should().Be(_project);
        new SettingsStore(Path.Combine(_host.Options.DataDirectory, SettingsStore.FileName)).Current.CurrentProject
            .Should().Be(_project, because: "다시 켜면 이 프로젝트로 연다");

        using (var clear = await PutAsync(null))
        {
            clear.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await GetAsync()).Current.Should().BeNull();
    }

    [Fact]
    public async Task A_path_that_is_not_in_the_list_is_refused()
    {
        using var put = await PutAsync(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: "화면이 보낸 아무 경로나 설정에 남기지 않는다");
        (await GetAsync()).Current.Should().BeNull();
    }

    private async Task<ProjectsResponse> GetAsync()
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, "/api/projects");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ProjectsResponse>())!;
    }

    private async Task<HttpResponseMessage> PutAsync(string? path)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Put, "/api/projects/current", _host.Url);
        request.Content = JsonContent.Create(new SetCurrentProjectRequest(path));

        return await client.SendAsync(request);
    }
}

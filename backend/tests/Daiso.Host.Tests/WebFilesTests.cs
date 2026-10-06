using System.Net;

namespace Daiso.Host.Tests;

/// <summary>웹 화면 파일을 내주는 것. 화면도 보안 미들웨어 뒤에 있다.</summary>
public sealed class WebFilesTests : IAsyncLifetime
{
    private RunningHost _host = null!;
    private string _web = null!;

    public async Task InitializeAsync()
    {
        _web = Path.Combine(Path.GetTempPath(), "daiso-host-tests", "web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_web, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_web, "index.html"), "<!doctype html><title>web</title>");
        await File.WriteAllTextAsync(Path.Combine(_web, "assets", "app.js"), "export {};");
        _host = await RunningHost.StartAsync(options => options with { WebRoot = _web });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_web, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sessions")]
    [InlineData("/usage")]
    public async Task Tab_addresses_get_the_index_page(string path)
    {
        var (status, body) = await GetAsync(path);

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("<title>web</title>", because: "주소로 탭을 가르므로 탭 주소로 새로 열어도 화면이 떠야 한다");
    }

    [Fact]
    public async Task Built_files_are_served()
    {
        var (status, body) = await GetAsync("/assets/app.js");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("export {};");
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/assets/missing.js")]
    public async Task Unknown_api_paths_and_missing_files_are_not_answered_with_the_page(string path)
    {
        var (status, _) = await GetAsync(path);

        status.Should().Be(HttpStatusCode.NotFound, because: "없는 API 에 화면 HTML 이 오면 화면이 JSON 대신 HTML 을 읽는다");
    }

    [Fact]
    public async Task The_page_needs_the_cookie_too()
    {
        using var client = _host.Client();
        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, path);
        using var response = await client.SendAsync(request);

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

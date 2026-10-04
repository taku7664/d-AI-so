using System.Diagnostics;
using Daiso.Host.Security;
using Daiso.Host.Tabs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Daiso.Host.Tests;

/// <summary>뜨고 끝나는 순서 (docs/SECURITY.md "서버를 띄우는 순서")와 띄울 때 받는 값.</summary>
public sealed class LifecycleTests
{
    [Fact]
    public async Task Server_json_is_removed_when_the_host_stops()
    {
        var host = await RunningHost.StartAsync();
        var path = host.InfoPath;
        File.Exists(path).Should().BeTrue();

        await host.App.StopAsync();

        File.Exists(path).Should().BeFalse(because: "꺼진 서버의 주소가 남으면 AI 가 죽은 주소를 연다");
        await host.DisposeAsync();
    }

    [Fact]
    public void Server_json_written_by_another_host_is_left_alone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daiso-server-{Guid.NewGuid():N}.json");
        ServerInfoFile.Write(path, new ServerInfo("http://127.0.0.1:1", "http://127.0.0.1:1/?token=x", "x", Environment.ProcessId + 1, DateTimeOffset.Now));

        ServerInfoFile.DeleteIfOwned(path, Environment.ProcessId);

        File.Exists(path).Should().BeTrue(because: "나중에 뜬 Host 의 파일이다");
        File.Delete(path);
    }

    [Fact]
    public async Task The_host_stops_when_its_parent_exits()
    {
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        await using var host = await RunningHost.StartAsync(options => options with { ParentPid = parent.Id });
        var stopping = new TaskCompletionSource();
        host.App.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        var finished = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        finished.Should().Be(stopping.Task, because: "부모가 끝나면 고아로 남지 않고 따라 끝나야 한다");
    }

    [Fact]
    public async Task The_host_stops_at_once_when_its_parent_is_already_gone()
    {
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        await gone.WaitForExitAsync();
        var pid = gone.Id;
        gone.Dispose();

        await using var host = await RunningHost.StartAsync(options => options with { ParentPid = pid });
        var stopping = new TaskCompletionSource();
        host.App.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        var finished = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        finished.Should().Be(stopping.Task);
    }

    [Fact]
    public void Without_a_token_the_host_makes_its_own_and_runs_standalone()
    {
        var options = DaisoHostOptions.Create(null, null, "data", "tools");

        options.Standalone.Should().BeTrue();
        options.Token.Length.Should().BeGreaterThanOrEqualTo(AccessToken.MinimumLength);
        options.ParentPid.Should().BeNull();
    }

    [Fact]
    public void A_token_from_electron_is_used_as_is()
    {
        var token = AccessToken.Create();

        var options = DaisoHostOptions.Create(token, "1234", "data", "tools");

        options.Standalone.Should().BeFalse();
        options.Token.Should().Be(token);
        options.ParentPid.Should().Be(1234);
    }

    [Theory]
    [InlineData("short", null)]
    [InlineData(null, "abc")]
    [InlineData(null, "-5")]
    public void A_weak_token_or_a_bad_parent_pid_stops_the_host_from_starting(string? token, string? parentPid)
    {
        var create = () => DaisoHostOptions.Create(token, parentPid, "data", "tools");

        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_tokens_are_never_the_same()
    {
        AccessToken.Create().Should().NotBe(AccessToken.Create());
    }

    [Theory]
    [InlineData("Sessions")]
    [InlineData("health")]
    [InlineData("")]
    public void A_tab_with_a_bad_or_taken_id_is_refused_before_the_host_starts(string id)
    {
        var build = () => DaisoHost.Build(
            new DaisoHostOptions { Token = AccessToken.Create(), DataDirectory = "data", PluginDirectory = "tools" },
            services => services.AddSingleton<ITabEndpoints>(new NamedTab(id)));

        build.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Two_tabs_with_the_same_id_are_refused()
    {
        var build = () => DaisoHost.Build(
            new DaisoHostOptions { Token = AccessToken.Create(), DataDirectory = "data", PluginDirectory = "tools" },
            services =>
            {
                services.AddSingleton<ITabEndpoints>(new NamedTab("sessions"));
                services.AddSingleton<ITabEndpoints>(new NamedTab("sessions"));
            });

        build.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task The_openapi_document_lists_health_and_tab_routes()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Get, "/openapi/v1.json");

        using var response = await client.SendAsync(request);
        var document = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        document.Should().Contain("\"/api/health\"").And.Contain("\"/api/probe/echo\"");
        document.Should().NotContain("\"/ws\"", because: "WebSocket 은 OpenAPI 로 타입을 만들 대상이 아니다");
    }

    private sealed class NamedTab(string id) : ITabEndpoints
    {
        public string Id => id;

        public void Map(RouteGroupBuilder group) => group.MapGet("/", () => id);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Daiso.Host.Notifications;
using Daiso.Host.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>
/// docs/SECURITY.md 의 일곱 가지를 하나씩 깨 본다. 번호는 그 문서의 번호다.
/// </summary>
public sealed class SecurityTests : IAsyncLifetime
{
    private RunningHost _host = null!;

    public async Task InitializeAsync() => _host = await RunningHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string SelfOrigin => _host.Url;

    // 1번 -----------------------------------------------------------------

    [Fact]
    public void Rule1_listens_on_127_0_0_1_with_an_os_chosen_port()
    {
        _host.Url.Should().StartWith("http://127.0.0.1:");
        _host.Port.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rule1_does_not_answer_on_ipv6_loopback()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

        var connect = async () => await socket.ConnectAsync(IPAddress.IPv6Loopback, _host.Port);

        await connect.Should().ThrowAsync<SocketException>(because: "localhost 가 ::1 로 풀려도 이 서버에 닿지 않아야 한다");
    }

    // 2·3번 ---------------------------------------------------------------

    [Fact]
    public async Task Rule3_every_route_refuses_a_request_without_the_cookie()
    {
        using var client = _host.Client();
        var routes = _host.Routes();

        routes.Should().Contain(route => route.Path == "/api/health");
        routes.Should().Contain(route => route.Path == "/ws");
        routes.Should().Contain(route => route.Method == "POST" && route.Path == "/api/probe/echo");

        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("Origin", SelfOrigin);

            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: $"{method} {path} 는 토큰 없이 열리면 안 된다");
        }
    }

    [Fact]
    public async Task Rule3_the_right_token_becomes_a_strict_http_only_cookie_and_leaves_the_address()
    {
        using var client = _host.Client();

        using var response = await client.GetAsync($"/?token={_host.Token}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/", because: "주소에서 토큰을 지워야 한다");

        var cookie = response.SetCookie();
        cookie.Should().StartWith($"{LocalOnlyMiddleware.CookieName(_host.Port)}={_host.Token}");
        cookie.Should().ContainEquivalentOf("httponly");
        cookie.Should().ContainEquivalentOf("samesite=strict");
        cookie.Should().NotContainEquivalentOf("expires", because: "세션 쿠키다. 브라우저를 닫으면 사라진다");
    }

    [Fact]
    public async Task Rule3_the_cookie_opens_health()
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, "/api/health");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"status":"ok"}""");
    }

    [Theory]
    [InlineData("wrong-token-wrong-token-wrong-token-xx")]
    [InlineData("")]
    public async Task Rule3_a_wrong_token_gets_no_cookie(string token)
    {
        using var client = _host.Client();

        using var response = await client.GetAsync($"/?token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.SetCookie().Should().BeNull();
    }

    [Fact]
    public async Task Rule3_a_wrong_cookie_is_refused()
    {
        using var client = _host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Cookie", $"{LocalOnlyMiddleware.CookieName(_host.Port)}={AccessToken.Create()}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rule3_the_token_is_taken_only_on_the_first_page()
    {
        using var client = _host.Client();

        using var response = await client.GetAsync($"/api/health?token={_host.Token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "주소의 토큰은 / 에서만 받는다");
        response.SetCookie().Should().BeNull();
    }

    [Fact]
    public async Task Rule3_a_cookie_for_another_port_is_not_this_servers_cookie()
    {
        using var client = _host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Cookie", $"{LocalOnlyMiddleware.CookieName(_host.Port + 1)}={_host.Token}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 4번 -----------------------------------------------------------------

    [Theory]
    [InlineData("localhost:{port}")]
    [InlineData("evil.example:{port}")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:1")]
    public async Task Rule4_every_route_refuses_a_foreign_host_header(string host)
    {
        using var client = _host.Client();

        foreach (var (method, path) in _host.Routes())
        {
            using var request = _host.Authed(new HttpMethod(method), path, SelfOrigin);
            request.Host(host.Replace("{port}", _host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{method} {path} 에 Host {request.Headers.Host} 로 오면 DNS rebinding 이다");
        }
    }

    [Fact]
    public async Task Rule4_a_foreign_host_cannot_even_trade_the_token()
    {
        using var client = _host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/?token={_host.Token}");
        request.Host($"evil.example:{_host.Port}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.SetCookie().Should().BeNull();
    }

    // 5번 -----------------------------------------------------------------

    [Fact]
    public async Task Rule5_a_state_changing_request_from_this_origin_goes_through()
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Post, "/api/probe/echo", SelfOrigin);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:{port}")]
    [InlineData("http://127.0.0.1:1")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Rule5_a_state_changing_request_from_another_origin_is_refused(string? origin)
    {
        using var client = _host.Client();
        using var request = _host.Authed(
            HttpMethod.Post,
            "/api/probe/echo",
            origin?.Replace("{port}", _host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rule5_websocket_with_cookie_and_this_origin_receives_notifications()
    {
        using var socket = Socket(SelfOrigin, withCookie: true);
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_host.Port}/ws"), CancellationToken.None);

        var hub = _host.App.Services.GetRequiredService<NotificationHub>();
        await WaitUntilAsync(() => hub.Count == 1);
        await hub.PublishAsync(new Notification("probe", "changed"), CancellationToken.None);

        var buffer = new byte[256];
        var received = await socket.ReceiveAsync(buffer, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        Encoding.UTF8.GetString(buffer, 0, received.Count).Should().Be("""{"tab":"probe","kind":"changed"}""");

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        await WaitUntilAsync(() => hub.Count == 0);
    }

    [Theory]
    [InlineData("http://evil.example", true, HttpStatusCode.Forbidden)]
    [InlineData(null, true, HttpStatusCode.Forbidden)]
    [InlineData("self", false, HttpStatusCode.Unauthorized)]
    public async Task Rule5_websocket_without_cookie_or_from_another_origin_is_refused(string? origin, bool withCookie, HttpStatusCode expected)
    {
        using var socket = Socket(origin == "self" ? SelfOrigin : origin, withCookie);

        var connect = async () => await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_host.Port}/ws"), CancellationToken.None);

        await connect.Should().ThrowAsync<WebSocketException>();
        socket.HttpStatusCode.Should().Be(expected);
    }

    // 6번 -----------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/health")]
    [InlineData("POST", "/api/probe/echo")]
    [InlineData("OPTIONS", "/api/health")]
    public async Task Rule6_no_response_opens_cors(string method, string path)
    {
        using var client = _host.Client();

        foreach (var origin in new[] { SelfOrigin, "http://evil.example" })
        {
            using var request = _host.Authed(new HttpMethod(method), path, origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");

            using var response = await client.SendAsync(request);

            response.HasCors().Should().BeFalse(because: $"{method} {path} (Origin {origin})");
        }
    }

    // 7번 -----------------------------------------------------------------

    [Fact]
    public void Rule7_server_json_holds_the_address_and_token_of_this_process()
    {
        var info = ServerInfoFile.Read(_host.InfoPath);

        info.Should().NotBeNull();
        info!.Url.Should().Be(_host.Url);
        info.Token.Should().Be(_host.Token);
        info.OpenUrl.Should().Be($"{_host.Url}/?token={_host.Token}");
        info.Pid.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public async Task Rule7_the_open_url_in_server_json_actually_opens()
    {
        var info = ServerInfoFile.Read(_host.InfoPath)!;
        using var client = _host.Client();

        using var response = await client.GetAsync(info.OpenUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.SetCookie().Should().NotBeNull();
    }

    private ClientWebSocket Socket(string? origin, bool withCookie)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;

        if (origin is not null)
        {
            socket.Options.SetRequestHeader("Origin", origin);
        }

        if (withCookie)
        {
            socket.Options.SetRequestHeader("Cookie", _host.CookieHeader);
        }

        return socket;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 5초 안에 맞지 않았다");
            }

            await Task.Delay(20);
        }
    }
}

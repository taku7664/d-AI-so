using System.Text.Json;
using Daiso.Host.Security;
using Daiso.Host.Services;
using Daiso.Infrastructure;
using Daiso.Host.Tabs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>
/// 진짜 Kestrel 로 띄운 Host. <c>Program</c> 과 같은 <see cref="DaisoHost.Build"/> 를 쓰고,
/// 자료 폴더와 플러그인 폴더만 임시 폴더로 바꾼다. 진짜 사용자 폴더는 건드리지 않는다.
/// </summary>
internal sealed class RunningHost : IAsyncDisposable
{
    private RunningHost(WebApplication app, DaisoHostOptions options)
    {
        App = app;
        Options = options;
        Url = DaisoHost.Url(app);
        Port = new Uri(Url).Port;
    }

    public WebApplication App { get; }

    public DaisoHostOptions Options { get; }

    /// <summary><c>http://127.0.0.1:{port}</c></summary>
    public string Url { get; }

    public int Port { get; }

    public string Token => Options.Token;

    public string CookieHeader => $"{LocalOnlyMiddleware.CookieName(Port)}={Token}";

    public string InfoPath => Path.Combine(Options.DataDirectory, ServerInfoFile.FileName);

    /// <param name="adjust">옵션을 바꾼다.</param>
    /// <param name="probe">시험용 탭(<see cref="ProbeTab"/>)을 꽂을지. OpenAPI 문서를 뜰 때는 뺀다.</param>
    /// <param name="settings">
    /// 처음 <c>settings.json</c>. 무엇을 주든 인덱스 경로와 세션 홈은 임시 폴더로 덮는다.
    /// 안 그러면 경로를 다 도는 보안 테스트가 진짜 사용자 인덱스(<c>%LOCALAPPDATA%\DAIso\index.db</c>)와 세션 폴더를 연다.
    /// </param>
    public static async Task<RunningHost> StartAsync(
        Func<DaisoHostOptions, DaisoHostOptions>? adjust = null,
        Action<AppSettings>? settings = null,
        bool probe = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "daiso-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(root, "data"));

        var seed = new AppSettings();
        settings?.Invoke(seed);
        seed.IndexDatabasePath = Path.Combine(root, "data", "index.db");
        seed.SessionHomeOverride = Path.Combine(root, "home");
        await File.WriteAllTextAsync(Path.Combine(root, "data", SettingsStore.FileName), JsonSerializer.Serialize(seed));

        var options = new DaisoHostOptions
        {
            Token = AccessToken.Create(),
            DataDirectory = Path.Combine(root, "data"),
            PluginDirectory = Path.Combine(root, "tools"),
        };

        options = adjust?.Invoke(options) ?? options;

        var app = DaisoHost.Build(options, services =>
        {
            if (probe)
            {
                services.AddSingleton<ITabEndpoints, ProbeTab>();
            }
        });

        // 임시 경로를 못 열면 IndexLocation 이 기본 경로(진짜 사용자 인덱스)로 물러선다. 그러면 시험을 멈춘다
        var index = app.Services.GetRequiredService<IndexLocation>();
        if (!index.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"시험 인덱스가 임시 폴더 밖이다: {index.Path} ({index.Failure})");
        }

        await app.StartAsync();

        return new RunningHost(app, options);
    }

    /// <summary>쿠키도 리다이렉트도 손으로 다루는 클라이언트. 무엇을 보냈는지가 시험의 전부라서.</summary>
    public HttpClient Client() => new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(Url),
    };

    /// <summary>토큰 쿠키를 실은 요청.</summary>
    public HttpRequestMessage Authed(HttpMethod method, string path, string? origin = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", CookieHeader);

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    /// <summary>
    /// 서버에 달린 경로 전부. 새 경로가 생기면 보안 테스트가 저절로 그 경로도 돈다 (docs/SECURITY.md).
    /// 경로 인자는 아무 값으로 채운다.
    /// </summary>
    public IReadOnlyList<(string Method, string Path)> Routes()
    {
        var routes = new List<(string, string)>();

        foreach (var source in App.Services.GetServices<EndpointDataSource>())
        {
            foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
            {
                var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                    string.Concat(segment.Parts.Select(part => part switch
                    {
                        Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
                        Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter => "v1",
                        _ => "x",
                    }))));

                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];

                routes.AddRange(methods.Select(method => (method, path)));
            }
        }

        return routes;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();

        try
        {
            Directory.Delete(Path.GetDirectoryName(Options.DataDirectory)!, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더다. 못 지워도 시험 결과와 상관없다
        }
    }
}

/// <summary>시험용 탭. 상태를 바꾸는 요청(POST)과 경로 인자가 있는 요청을 하나씩 단다.</summary>
internal sealed class ProbeTab : ITabEndpoints
{
    public string Id => "probe";

    public void Map(RouteGroupBuilder group)
    {
        group.MapGet("/items/{id}", (string id) => id);
        group.MapPost("/echo", () => "echo");
    }
}

internal static class HttpExtensions
{
    public static string? SetCookie(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join("\n", values) : null;

    public static bool HasCors(this HttpResponseMessage response) =>
        response.Headers.NonValidated.Contains("Access-Control-Allow-Origin")
        || response.Content.Headers.NonValidated.Contains("Access-Control-Allow-Origin");

    public static void Host(this HttpRequestMessage request, string host) => request.Headers.Host = host;
}

using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Daiso.Host.Notifications;
using Daiso.Host.Security;
using Daiso.Host.Shared;
using Daiso.Host.Services;
using Daiso.Host.Tabs;
using Daiso.Host.Tabs.Dashboard;
using Daiso.Host.Tabs.Sessions;
using Daiso.Host.Tabs.Terminal;
using Daiso.Host.Tabs.Usage;

namespace Daiso.Host;

/// <summary>헬스 체크 응답.</summary>
/// <param name="Status">살아 있으면 <c>ok</c>.</param>
public sealed record HealthResponse(string Status);

/// <summary>
/// Host 를 조립한다. <c>Program</c> 과 테스트가 같은 조립을 쓴다 — 테스트만 다른 길로 띄우면
/// 보안 검사가 빠진 서버를 시험하게 된다.
/// </summary>
public static class DaisoHost
{
    /// <summary>
    /// 서버를 만든다. 아직 띄우지는 않는다.
    /// </summary>
    /// <param name="options">토큰·자료 폴더 등.</param>
    /// <param name="configureServices">테스트가 탭을 더 꽂을 때 쓴다.</param>
    public static WebApplication Build(DaisoHostOptions options, Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // 1번. 127.0.0.1 에만 연다. localhost 는 IPv6(::1)로 풀릴 수 있어서 쓰지 않는다
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, options.Port));

        // 요청 기록에는 첫 접속 주소(토큰이 붙은)가 그대로 찍힌다. 표준 출력은 Electron 이 읽으므로 기록을 줄인다
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        builder.Services.AddSingleton(options);
        // 웹 기본값은 숫자를 문자열로도 받는다. 그러면 OpenAPI 타입이 number | string 이 되어 화면이 두 경우를 다 다뤄야 한다
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
        builder.Services.AddSingleton<NotificationHub>();
        builder.Services.AddSingleton<ServerAnnouncer>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ServerAnnouncer>());
        builder.Services.AddHostedService<ParentWatcher>();
        // 문서 제목은 프로세스 이름을 따라간다(시험에서는 testhost). 웹이 쓰는 문서가 띄우는 법에 따라 달라지지 않게 박는다
        builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "DAIso";
            return Task.CompletedTask;
        }));
        builder.Services.AddDaisoDomain(options);
        builder.Services.AddSingleton<ProjectCatalog>();
        builder.Services.AddSingleton<IndexService>();
        builder.Services.AddSingleton<ClaudeStatusLine>();
        builder.Services.AddSingleton<LimitsService>();
        builder.Services.AddHostedService<LimitsWatcher>();
        builder.Services.AddHostedService<IndexRefreshOnStart>();
        builder.Services.AddSingleton<ITabEndpoints, UsageEndpoints>();
        builder.Services.AddSingleton<ITabEndpoints, SessionsEndpoints>();
        builder.Services.AddSingleton<ITabEndpoints, DashboardEndpoints>();
        builder.Services.AddSingleton<ITabEndpoints, TerminalEndpoints>();
        builder.Services.AddSingleton<RoomService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<RoomService>());
        builder.Services.AddSingleton<SessionNames>();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        // 6번. CORS 미들웨어를 넣지 않는다. Access-Control-Allow-Origin 이 나가지 않는다
        app.UseWebSockets();
        app.UseMiddleware<LocalOnlyMiddleware>();

        var web = WebFiles(options);
        if (web is not null)
        {
            // 화면 파일도 미들웨어 뒤에 둔다. 쿠키 없이는 화면도 못 받는다
            app.UseStaticFiles(new StaticFileOptions { FileProvider = web });
        }

        app.MapGet("/api/health", () => new HealthResponse("ok")).WithName("Health").WithTags("health");
        app.MapOpenApi();
        app.Map("/ws", HoldNotificationsAsync).ExcludeFromDescription();
        app.MapShared();
        app.MapAccounts();
        app.MapTabs();

        if (web is null)
        {
            app.MapGet("/", () => Results.Content(LandingPage, "text/html; charset=utf-8")).ExcludeFromDescription();
        }
        else
        {
            // 화면은 주소로 탭을 가른다(/sessions 등). 그 주소로 새로 열어도 index.html 을 낸다. /api 아래는 해당하지 않는다
            var index = web.GetFileInfo("index.html");
            app.MapGet("/", () => IndexHtml(index)).ExcludeFromDescription();
            app.MapFallback("{*path:nonfile}", () => IndexHtml(index)).ExcludeFromDescription();
            app.MapFallback("/api/{*rest}", () => Results.NotFound()).ExcludeFromDescription();
        }

        return app;
    }

    /// <summary>뜬 주소. <see cref="WebApplication.StartAsync"/> 뒤에만 부른다.</summary>
    public static string Url(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Services.GetRequiredService<ServerAnnouncer>().Url
            ?? throw new InvalidOperationException("서버가 아직 뜨지 않았다");
    }

    /// <summary>웹 화면 폴더. 지정하지 않았거나 <c>index.html</c> 이 없으면 null.</summary>
    private static PhysicalFileProvider? WebFiles(DaisoHostOptions options) =>
        options.WebRoot is { } root && File.Exists(Path.Combine(root, "index.html"))
            ? new PhysicalFileProvider(Path.GetFullPath(root))
            : null;

    /// <summary>
    /// <c>index.html</c>. 캐시하지 않는다 — 빌드가 바뀌면 이름에 해시가 붙은 새 자산을 가리키므로 이것만 늘 새로 받으면 된다.
    /// </summary>
    private static IResult IndexHtml(IFileInfo index) =>
        Results.Stream(index.CreateReadStream(), "text/html; charset=utf-8");

    private static async Task HoldNotificationsAsync(HttpContext context, NotificationHub hub)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await hub.HoldAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>웹 화면 폴더가 없을 때(혼자 띄운 개발용 Host, 테스트) 보여 주는 첫 화면.</summary>
    private const string LandingPage = """
        <!doctype html>
        <html lang="ko">
        <meta charset="utf-8">
        <title>DAIso Host</title>
        <h1>DAIso Host</h1>
        <p>웹 화면 폴더(DAISO_WEB_ROOT)가 없어서 화면을 내지 않는다.</p>
        <ul>
          <li><a href="/api/health">/api/health</a></li>
          <li><a href="/openapi/v1.json">/openapi/v1.json</a></li>
        </ul>
        </html>
        """;
}

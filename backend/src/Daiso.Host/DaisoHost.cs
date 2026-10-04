using System.Net;
using Daiso.Host.Notifications;
using Daiso.Host.Security;
using Daiso.Host.Tabs;

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
        builder.Services.AddSingleton<NotificationHub>();
        builder.Services.AddSingleton<ServerAnnouncer>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ServerAnnouncer>());
        builder.Services.AddHostedService<ParentWatcher>();
        builder.Services.AddOpenApi();
        builder.Services.AddDaisoDomain(options);

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        // 6번. CORS 미들웨어를 넣지 않는다. Access-Control-Allow-Origin 이 나가지 않는다
        app.UseWebSockets();
        app.UseMiddleware<LocalOnlyMiddleware>();

        app.MapGet("/", () => Results.Content(LandingPage, "text/html; charset=utf-8")).ExcludeFromDescription();
        app.MapGet("/api/health", () => new HealthResponse("ok")).WithName("Health");
        app.MapOpenApi();
        app.Map("/ws", HoldNotificationsAsync).ExcludeFromDescription();
        app.MapTabs();

        return app;
    }

    /// <summary>뜬 주소. <see cref="WebApplication.StartAsync"/> 뒤에만 부른다.</summary>
    public static string Url(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Services.GetRequiredService<ServerAnnouncer>().Url
            ?? throw new InvalidOperationException("서버가 아직 뜨지 않았다");
    }

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

    /// <summary>Stage 3 에서 웹 화면이 들어오기 전까지 보여 주는 첫 화면.</summary>
    private const string LandingPage = """
        <!doctype html>
        <html lang="ko">
        <meta charset="utf-8">
        <title>DAIso Host</title>
        <h1>DAIso Host</h1>
        <p>화면은 아직 없다 (docs/ROADMAP.md Stage 3).</p>
        <ul>
          <li><a href="/api/health">/api/health</a></li>
          <li><a href="/openapi/v1.json">/openapi/v1.json</a></li>
        </ul>
        </html>
        """;
}

using System.Globalization;

namespace Daiso.Host.Security;

/// <summary>
/// 모든 요청이 지나는 보안 검사 (docs/SECURITY.md 3·4·5번). 엔드포인트마다 넣지 않고 여기 한 곳에만 둔다.
/// <para>
/// 순서: <c>Host</c> 헤더 → 첫 접속 토큰을 쿠키로 바꾸기 → 쿠키 → (상태를 바꾸는 요청·WebSocket 이면) <c>Origin</c>.
/// <c>Host</c> 를 먼저 보는 것은 DNS rebinding 으로 들어온 페이지가 토큰 교환까지도 못 닿게 하려는 것이다.
/// </para>
/// </summary>
public sealed class LocalOnlyMiddleware
{
    /// <summary>첫 접속에서 토큰을 싣는 쿼리 이름.</summary>
    public const string TokenQuery = "token";

    private readonly RequestDelegate _next;
    private readonly string _token;

    public LocalOnlyMiddleware(RequestDelegate next, DaisoHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _token = options.Token;
    }

    /// <summary>
    /// 쿠키 이름. 포트를 붙인다 — 쿠키는 포트를 가리지 않아서,
    /// 이름이 같으면 두 번째로 띄운 Host(개발 중 Electron 과 혼자 띄운 Host 등)가 앞의 쿠키를 덮는다.
    /// </summary>
    public static string CookieName(int port) => string.Create(CultureInfo.InvariantCulture, $"daiso_{port}");

    /// <summary>요청을 받은 자기 주소. 포트 0 으로 떴으므로 실제 포트는 연결에서 읽는다.</summary>
    public static string SelfAuthority(HttpContext context) =>
        string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{context.Connection.LocalPort}");

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var self = SelfAuthority(context);

        // 4번. localhost 도 받지 않는다. 브라우저 주소창과 Host 헤더가 정확히 이 값일 때만 우리 화면이다
        if (!string.Equals(context.Request.Host.Value, self, StringComparison.Ordinal))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Host 헤더가 이 서버 주소가 아니다").ConfigureAwait(false);
            return;
        }

        var cookieName = CookieName(context.Connection.LocalPort);

        // 3번. 첫 접속만 주소에 토큰을 싣는다. 쿠키로 바꾸고 주소에서 지운다 — 주소에 남으면 기록·화면 공유로 샌다
        if (context.Request.Path == "/" && context.Request.Query.TryGetValue(TokenQuery, out var offered))
        {
            if (!AccessToken.Matches(_token, offered.ToString()))
            {
                await RejectAsync(context, StatusCodes.Status401Unauthorized, "토큰이 맞지 않는다").ConfigureAwait(false);
                return;
            }

            context.Response.Cookies.Append(cookieName, _token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Secure = false,
                IsEssential = true,
            });
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Redirect("/");
            return;
        }

        if (!AccessToken.Matches(_token, context.Request.Cookies[cookieName]))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "토큰이 없다. 토큰이 붙은 주소로 처음 열어야 한다").ConfigureAwait(false);
            return;
        }

        // 5번. 쿠키는 브라우저가 알아서 싣는다. 다른 사이트가 보낸 요청인지는 Origin 으로 가른다.
        // Origin 이 없는 것도 거절한다 — 브라우저는 상태를 바꾸는 요청과 WebSocket 에 늘 Origin 을 싣는다
        if (NeedsOrigin(context) && !string.Equals(context.Request.Headers.Origin.ToString(), "http://" + self, StringComparison.Ordinal))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Origin 이 이 서버 주소가 아니다").ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool NeedsOrigin(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest
        || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));

    private static Task RejectAsync(HttpContext context, int status, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";

        return context.Response.WriteAsync(reason, System.Text.Encoding.UTF8, context.RequestAborted);
    }
}

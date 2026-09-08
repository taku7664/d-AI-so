using System.Globalization;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Gemini;

/// <summary>
/// `~/.gemini/oauth_creds.json`과 `~/.gemini/google_accounts.json`을 읽어 <see cref="AuthStatus"/>를 만든다. (ARCHITECTURE §4.5 인증)
/// 토큰 값은 어떤 필드에도 넣지 않는다. 만료 시각과 계정 이메일만 뽑는다.
/// </summary>
public static class GeminiAuthReader
{
    /// <param name="oauthJson">`oauth_creds.json` 내용. null이면 로그인 정보 없음.</param>
    /// <param name="accountsJson">`google_accounts.json` 내용. 계정 이메일 표시용. 없으면 null.</param>
    /// <param name="now">상태 판정 기준 시각.</param>
    public static AuthStatus Read(string? oauthJson, string? accountsJson, DateTimeOffset now)
    {
        if (oauthJson is null)
        {
            return AuthStatus.Missing(ToolKind.Gemini);
        }

        using var document = JsonHelpers.TryParseLine(oauthJson);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return AuthStatus.Missing(ToolKind.Gemini);
        }

        // refresh_token이 있으면 액세스 토큰이 만료돼도 CLI가 알아서 갱신한다. 그때는 만료 개념이 없다.
        var hasRefresh = root.Prop("refresh_token") is { ValueKind: JsonValueKind.String };
        var accessExpiry = root.Prop("expiry_date").Number() is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : (DateTimeOffset?)null;
        var expiresAt = hasRefresh ? null : accessExpiry;

        var email = ActiveEmail(accountsJson);

        // 라벨은 계정 제공자, 이메일은 이메일. 같은 값을 두 줄에 쓰지 않는다
        return new AuthStatus(
            ToolKind.Gemini,
            AuthStatus.StateFor(expiresAt, now),
            "Google",
            email,
            expiresAt,
            Extras(root, accessExpiry, hasRefresh));
    }

    private static string? ActiveEmail(string? accountsJson)
    {
        if (accountsJson is null)
        {
            return null;
        }

        using var document = JsonHelpers.TryParseLine(accountsJson);
        return document?.RootElement.Prop("active").Text();
    }

    private static IReadOnlyList<string> Extras(JsonElement root, DateTimeOffset? accessExpiry, bool hasRefresh)
    {
        var extras = new List<string>();

        if (root.Prop("token_type").Text() is { } tokenType)
        {
            extras.Add($"token_type: {tokenType}");
        }

        extras.Add(hasRefresh ? "refresh_token: 있음" : "refresh_token: 없음");

        if (accessExpiry is { } expiry)
        {
            extras.Add($"access_token 만료: {expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        }

        // scope 값은 URL 목록이라 길다. 개수만 알린다.
        if (root.Prop("scope").Text() is { } scope)
        {
            var count = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            extras.Add($"scope: {count}개");
        }

        return extras;
    }
}

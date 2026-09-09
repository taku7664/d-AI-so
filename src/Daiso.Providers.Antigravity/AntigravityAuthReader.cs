using System.Globalization;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Antigravity;

/// <summary>
/// Antigravity CLI 의 로그인 상태를 만든다. (ARCHITECTURE §4.5 인증)
/// 토큰 값은 어떤 필드에도 넣지 않는다. 만료 시각과 계정 이메일만 뽑는다.
/// <para>
/// <b>Antigravity CLI 는 토큰을 파일이 아니라 Windows 자격 증명 관리자에 넣는다.</b> 앱은 그것을 읽지 않는다.
/// 그래서 읽을 수 있는 것은 둘뿐이다 — 은퇴한 Gemini CLI 가 남긴 `~/.gemini/oauth_creds.json`(있으면 계정·만료를 알 수 있다)와
/// `~/.gemini/antigravity-cli/settings.json`(있으면 `agy` 를 설정한 적이 있다는 뜻). 만료를 모르는 경우는 모른다고 적는다.
/// </para>
/// </summary>
public static class AntigravityAuthReader
{
    /// <param name="oauthJson">은퇴한 Gemini CLI 의 `oauth_creds.json` 내용. 없으면 null.</param>
    /// <param name="accountsJson">`google_accounts.json` 내용. 계정 이메일 표시용. 없으면 null.</param>
    /// <param name="settingsJson">`antigravity-cli/settings.json` 내용. 있으면 `agy` 를 설정한 적이 있다는 뜻. 없으면 null.</param>
    /// <param name="now">상태 판정 기준 시각.</param>
    public static AuthStatus Read(string? oauthJson, string? accountsJson, string? settingsJson, DateTimeOffset now)
    {
        var configured = settingsJson is not null;

        if (oauthJson is null)
        {
            // 읽을 파일이 하나도 없으면 로그인 정보 없음. 설정 파일만 있으면 "설정은 했고 만료는 모른다"로 둔다
            return configured
                ? new AuthStatus(
                    ToolKind.Antigravity,
                    AuthState.LoggedIn,
                    "Google",
                    null,
                    null,
                    KeyringExtras(settingsJson))
                : AuthStatus.Missing(ToolKind.Antigravity);
        }

        using var document = JsonHelpers.TryParseLine(oauthJson);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return AuthStatus.Missing(ToolKind.Antigravity);
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
            ToolKind.Antigravity,
            AuthStatus.StateFor(expiresAt, now),
            "Google",
            email,
            expiresAt,
            [.. Extras(root, accessExpiry, hasRefresh), .. KeyringExtras(settingsJson)]);
    }

    /// <summary>
    /// Antigravity 쪽에서만 알 수 있는 것들. 만료를 화면에 못 적는 이유를 여기서 밝힌다 —
    /// 카드에 아무 말이 없으면 사용자는 앱이 못 읽는 것인지 로그인이 안 된 것인지 구분할 수 없다.
    /// </summary>
    private static IReadOnlyList<string> KeyringExtras(string? settingsJson)
    {
        var extras = new List<string>
        {
            "Antigravity 로그인: Windows 자격 증명 관리자에 보관 (앱이 읽지 않음)",
        };

        if (settingsJson is null)
        {
            extras.Add("antigravity-cli/settings.json: 없음");
            return extras;
        }

        using var document = JsonHelpers.TryParseLine(settingsJson);
        var hasApiKey = document?.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.Prop("apiKey") is { ValueKind: JsonValueKind.String };

        extras.Add(hasApiKey ? "인증 방식: API 키" : "인증 방식: 브라우저 로그인");

        return extras;
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

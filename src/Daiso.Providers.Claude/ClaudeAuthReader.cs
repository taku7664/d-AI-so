using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Claude;

/// <summary>
/// Claude 인증 파일 두 개를 읽어 <see cref="AuthStatus"/>를 만든다. (ARCHITECTURE §4.1 인증)
/// 토큰 값은 어떤 필드에도 넣지 않는다.
/// </summary>
public static class ClaudeAuthReader
{
    /// <summary>
    /// 재로그인이 필요해지는 시각은 `refreshTokenExpiresAt`이다.
    /// `expiresAt`은 자동 갱신되는 단기 액세스 토큰이라 상태 판정에 쓰지 않는다.
    /// </summary>
    /// <param name="credentialsJson">`.credentials.json` 내용. null이면 로그인 정보 없음.</param>
    /// <param name="claudeJson">`.claude.json` 내용. 계정 표시용. 없으면 null.</param>
    /// <param name="now">상태 판정 기준 시각.</param>
    public static AuthStatus Read(string? credentialsJson, string? claudeJson, DateTimeOffset now)
    {
        if (credentialsJson is null)
        {
            return AuthStatus.Missing(ToolKind.Claude);
        }

        using var credentials = JsonHelpers.TryParseLine(credentialsJson);
        var oauth = credentials?.RootElement.Prop("claudeAiOauth");

        if (oauth is not { ValueKind: JsonValueKind.Object })
        {
            return AuthStatus.Missing(ToolKind.Claude);
        }

        var expiresAt = oauth.Prop("refreshTokenExpiresAt").Number() is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : (DateTimeOffset?)null;

        var extras = Extras(oauth.Value);
        var (label, email) = Account(claudeJson, oauth.Prop("subscriptionType").Text());

        return new AuthStatus(
            ToolKind.Claude,
            AuthStatus.StateFor(expiresAt, now),
            label,
            email,
            expiresAt,
            extras);
    }

    private static IReadOnlyList<AuthNote> Extras(JsonElement oauth)
    {
        var extras = new List<AuthNote>();

        if (oauth.Prop("subscriptionType").Text() is { } subscription)
        {
            extras.Add(new AuthNote("AuthNote_Subscription", subscription));
        }

        if (oauth.Prop("rateLimitTier").Text() is { } tier)
        {
            extras.Add(new AuthNote("AuthNote_RateLimitTier", tier));
        }

        foreach (var scope in oauth.Prop("scopes").Items())
        {
            if (scope.ValueKind == JsonValueKind.String && scope.GetString() is { Length: > 0 } text)
            {
                extras.Add(new AuthNote("AuthNote_Scope", text));
            }
        }

        // mcpOAuth는 키 이름의 '|' 앞부분(커넥터 이름)만 쓴다. 값은 토큰이라 읽지 않는다.
        if (oauth.Prop("mcpOAuth") is { ValueKind: JsonValueKind.Object } mcp)
        {
            foreach (var connector in mcp.EnumerateObject())
            {
                var name = connector.Name.Split('|')[0];
                if (name.Length > 0)
                {
                    extras.Add(new AuthNote("AuthNote_Mcp", name));
                }
            }
        }

        return extras;
    }

    private static (string? Label, string? Email) Account(string? claudeJson, string? subscriptionType)
    {
        if (claudeJson is null)
        {
            return (null, null);
        }

        using var document = JsonHelpers.TryParseLine(claudeJson);
        var account = document?.RootElement.Prop("oauthAccount");

        if (account is not { ValueKind: JsonValueKind.Object })
        {
            return (null, null);
        }

        var parts = new[]
        {
            account.Prop("displayName").Text(),
            account.Prop("organizationName").Text(),
            subscriptionType,
        }.Where(part => part is not null);

        var label = string.Join(" · ", parts);

        return (label.Length == 0 ? null : label, account.Prop("emailAddress").Text());
    }
}

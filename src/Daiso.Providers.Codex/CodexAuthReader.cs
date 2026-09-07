using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Codex;

/// <summary>
/// `~/.codex/auth.json`을 읽어 <see cref="AuthStatus"/>를 만든다. (ARCHITECTURE §4.2 인증)
/// 토큰 원문은 만료 시각만 뽑고 즉시 버린다.
/// </summary>
public static class CodexAuthReader
{
    private const string ApiKeyMode = "apikey";

    /// <param name="authJson">`auth.json` 내용. null이면 로그인 정보 없음.</param>
    /// <param name="now">상태 판정 기준 시각.</param>
    public static AuthStatus Read(string? authJson, DateTimeOffset now)
    {
        if (authJson is null)
        {
            return AuthStatus.Missing(ToolKind.Codex);
        }

        using var document = JsonHelpers.TryParseLine(authJson);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return AuthStatus.Missing(ToolKind.Codex);
        }

        var mode = root.Prop("auth_mode").Text();
        var tokens = root.Prop("tokens");

        // API 키 모드는 만료 개념이 없다.
        var expiresAt = string.Equals(mode, ApiKeyMode, StringComparison.OrdinalIgnoreCase)
            ? null
            : JwtExpiry.Read(tokens.Prop("access_token").Text());

        return new AuthStatus(
            ToolKind.Codex,
            AuthStatus.StateFor(expiresAt, now),
            mode,
            null,
            expiresAt,
            Extras(root, tokens, mode));
    }

    private static IReadOnlyList<string> Extras(JsonElement root, JsonElement? tokens, string? mode)
    {
        var extras = new List<string>();

        if (mode is not null)
        {
            extras.Add($"auth_mode: {mode}");
        }

        if (root.Prop("last_refresh").Text() is { } lastRefresh)
        {
            extras.Add($"last_refresh: {lastRefresh}");
        }

        // 키 값은 절대 담지 않는다. 설정 여부만 알린다.
        extras.Add(root.Prop("OPENAI_API_KEY") is { ValueKind: JsonValueKind.String }
            ? "OPENAI_API_KEY: 설정됨"
            : "OPENAI_API_KEY: 없음");

        if (tokens.Prop("account_id").Text() is { } accountId)
        {
            extras.Add($"account_id: {accountId}");
        }

        return extras;
    }
}

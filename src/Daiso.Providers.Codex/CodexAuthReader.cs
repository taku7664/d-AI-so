using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Codex;

/// <summary>
/// `~/.codex/auth.json`을 읽어 <see cref="AuthStatus"/>를 만든다. (ARCHITECTURE §4.2 인증)
/// 토큰 원문은 만료 시각·계정 표시값만 뽑고 즉시 버린다.
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
        var isApiKey = string.Equals(mode, ApiKeyMode, StringComparison.OrdinalIgnoreCase);

        // API 키 모드는 만료 개념이 없다.
        var expiresAt = isApiKey ? null : JwtReader.Expiry(tokens.Prop("access_token").Text());

        // 계정은 id 토큰이 말한다. 인증 방식(`chatgpt`)을 계정 이름 자리에 넣지 않는다 — 그것은 계정이 아니다
        var (email, plan) = JwtReader.Account(tokens.Prop("id_token").Text());

        return new AuthStatus(
            ToolKind.Codex,
            AuthStatus.StateFor(expiresAt, now),
            plan ?? email,
            email,
            expiresAt,
            Extras(root, mode, isApiKey, plan));
    }

    /// <summary>
    /// 카드에 한 줄씩 붙는 부가 정보.
    /// <para>
    /// <b>JSON 필드 이름을 그대로 뱉지 않는다.</b> 예전에는 `auth_mode: chatgpt`·`account_id: …` 처럼
    /// 파일에 적힌 말을 옮겼는데, 같은 자리에 다른 도구는 사람이 읽는 문장을 넣고 있어 결이 어긋났다
    /// (2026-09-11 사람의 지적). `account_id` 는 사람이 쓸 데가 없어 아예 뺐다.
    /// </para>
    /// </summary>
    private static IReadOnlyList<AuthNote> Extras(JsonElement root, string? mode, bool isApiKey, string? plan)
    {
        var extras = new List<AuthNote>();

        // 아는 방식은 문구 키로 바꾸고, 모르는 값이면 값을 그대로 보여 준다 — 새 방식이 생겼을 때 침묵하지 않게
        extras.Add(mode switch
        {
            null => new AuthNote("AuthNote_AuthModeUnknown"),
            _ when isApiKey => new AuthNote("AuthNote_AuthApiKey"),
            "chatgpt" => new AuthNote("AuthNote_AuthChatGpt"),
            _ => new AuthNote("AuthNote_AuthMode", mode),
        });

        if (plan is { } value)
        {
            extras.Add(new AuthNote("AuthNote_Plan", value));
        }

        if (root.Prop("last_refresh").Text() is { } lastRefresh)
        {
            extras.Add(new AuthNote("AuthNote_LastRefresh", lastRefresh));
        }

        // 키 값은 절대 담지 않는다. 설정 여부만 알린다.
        extras.Add(new AuthNote(root.Prop("OPENAI_API_KEY") is { ValueKind: JsonValueKind.String }
            ? "AuthNote_ApiKeySet"
            : "AuthNote_ApiKeyMissing"));

        return extras;
    }
}

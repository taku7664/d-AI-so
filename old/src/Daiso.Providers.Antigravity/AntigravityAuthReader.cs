using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Antigravity;

/// <summary>
/// Antigravity CLI 의 로그인 상태를 만든다. (ARCHITECTURE §4.5 인증)
/// <para>
/// <b>판정의 근거는 Windows 자격 증명 관리자의 항목 하나(`gemini:antigravity`)뿐이다.</b> `agy` 는 토큰을 파일에 남기지 않는다 —
/// 로그인하고 대화까지 해도 `~/.gemini/antigravity-cli` 에는 토큰이 없다(2026-09-09 실측). 그 항목이 있으면 로그인, 없으면 없음이다.
/// 값은 읽지 않는다(<see cref="Daiso.Providers.Common.ICredentialProbe"/>).
/// </para>
/// <para>
/// <b>은퇴한 Gemini CLI 의 `oauth_creds.json` 은 이 도구의 상태가 아니다.</b> 전에는 그 파일의 `refresh_token`·`expiry_date`·`scope` 를
/// Antigravity 카드에 그대로 얹었는데, 죽은 도구의 액세스 토큰 만료 시각을 이 도구의 것처럼 보여 주는 셈이었다.
/// 이제는 "그 파일이 남아 있다"는 사실만, 무관하다고 밝혀 적는다. 계정 이메일도 그 파일에서 끌어오지 않는다 —
/// 다른 계정으로 `agy` 에 로그인했으면 틀린 이메일을 보여 준다.
/// </para>
/// <para>
/// 만료는 알 수 없다. `agy` 가 알아서 갱신하고 앱은 그 시각을 볼 수 없으므로 <c>SessionExpiresAt = null</c> 이다.
/// 화면이 이유를 말하도록 부가 정보에 적는다.
/// </para>
/// </summary>
public static class AntigravityAuthReader
{
    /// <summary>자격 증명 관리자에서 찾는 대상 이름. `cmdkey /list` 에 `LegacyGeneric:target=gemini:antigravity` 로 보인다.</summary>
    public const string CredentialTarget = "gemini:antigravity";

    /// <param name="hasCredential">자격 증명 관리자에 <see cref="CredentialTarget"/> 항목이 있는가. 로그인 판정의 근거다.</param>
    /// <param name="settingsJson">`antigravity-cli/settings.json` 내용. 없으면 null. `apiKey` 가 있으면 API 키 방식이다.</param>
    /// <param name="hasLegacyGeminiLogin">은퇴한 Gemini CLI 의 `oauth_creds.json` 이 남아 있는가. 표시용일 뿐 판정에 쓰지 않는다.</param>
    public static AuthStatus Read(bool hasCredential, string? settingsJson, bool hasLegacyGeminiLogin)
    {
        var extras = Extras(settingsJson, hasLegacyGeminiLogin);

        if (!hasCredential)
        {
            return new AuthStatus(ToolKind.Antigravity, AuthState.Missing, null, null, null, extras);
        }

        // 이메일을 읽을 곳이 없다. 라벨만 제작사로 두고, 이메일 자리는 비운다(틀린 값을 채우지 않는다)
        return new AuthStatus(ToolKind.Antigravity, AuthState.LoggedIn, "Google", null, null, extras);
    }

    private static IReadOnlyList<AuthNote> Extras(string? settingsJson, bool hasLegacyGeminiLogin)
    {
        var extras = new List<AuthNote>
        {
            new("AuthNote_CredentialStore", CredentialTarget),
            new("AuthNote_ExpiryUnknown"),
            new("AuthNote_NoEmailInFiles"),
        };

        if (settingsJson is null)
        {
            extras.Add(new AuthNote("AuthNote_NoSettingsFile"));
        }
        else
        {
            using var document = JsonHelpers.TryParseLine(settingsJson);
            var root = document?.RootElement;

            var hasApiKey = root is { ValueKind: JsonValueKind.Object } obj
                && obj.Prop("apiKey") is { ValueKind: JsonValueKind.String };

            extras.Add(new AuthNote(hasApiKey ? "AuthNote_AuthApiKey" : "AuthNote_AuthBrowser"));

            if (root is { ValueKind: JsonValueKind.Object } settings && settings.Prop("model").Text() is { } model)
            {
                extras.Add(new AuthNote("AuthNote_Model", model));
            }
        }

        if (hasLegacyGeminiLogin)
        {
            extras.Add(new AuthNote("AuthNote_LegacyGeminiLogin"));
        }

        return extras;
    }
}

using Daiso.Core;
using Daiso.Providers.Antigravity;

namespace Daiso.Providers.Tests.Antigravity;

/// <summary>
/// ARCHITECTURE §4.5 인증. `agy` 는 토큰을 파일에 남기지 않는다 —
/// 로그인 판정의 근거는 Windows 자격 증명 관리자의 항목 하나뿐이고, 만료는 알 수 없다.
/// </summary>
public sealed class AntigravityAuthTests
{
    private const string Settings = """{"colorScheme":"dark","model":"Gemini 3.8 Flash"}""";

    [Fact]
    public void No_credential_means_Missing()
    {
        var status = AntigravityAuthReader.Read(hasCredential: false, Settings, hasLegacyGeminiLogin: false);

        status.State.Should().Be(AuthState.Missing);
        status.AccountLabel.Should().BeNull();
    }

    /// <summary>설정 파일이 있어도 자격 증명이 없으면 로그인이 아니다. 설정은 "한 번 실행했다"는 뜻일 뿐이다.</summary>
    [Fact]
    public void A_settings_file_alone_is_not_a_login()
    {
        AntigravityAuthReader.Read(hasCredential: false, Settings, hasLegacyGeminiLogin: true)
            .State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public void A_credential_means_logged_in_without_an_expiry()
    {
        var status = AntigravityAuthReader.Read(hasCredential: true, Settings, hasLegacyGeminiLogin: false);

        status.State.Should().Be(AuthState.LoggedIn);
        status.AccountLabel.Should().Be("Google");
        status.SessionExpiresAt.Should().BeNull(because: "만료 시각을 앱이 볼 수 없다");
    }

    /// <summary>
    /// 이메일 자리는 비운다. 읽을 수 있는 것은 은퇴한 Gemini CLI 의 계정 파일뿐이고,
    /// 다른 계정으로 `agy` 에 로그인했으면 그 값은 틀린 값이다.
    /// </summary>
    [Fact]
    public void The_email_is_left_empty_instead_of_borrowing_the_retired_tools_account()
    {
        AntigravityAuthReader.Read(hasCredential: true, Settings, hasLegacyGeminiLogin: true)
            .Email.Should().BeNull();
    }

    /// <summary>
    /// 화면이 "왜 만료를 못 보여 주는지"와 "어디에 보관되는지"를 말해야 한다.
    /// 아무 말이 없으면 앱이 못 읽는 것인지 로그인이 안 된 것인지 구분할 수 없다.
    /// </summary>
    [Fact]
    public void Extras_explain_where_the_login_lives_and_why_there_is_no_expiry()
    {
        var extras = AntigravityAuthReader.Read(hasCredential: true, Settings, hasLegacyGeminiLogin: false).Extras;

        extras.Should().Contain(new AuthNote("AuthNote_CredentialStore", AntigravityAuthReader.CredentialTarget));
        extras.Should().Contain(note => note.Key == "AuthNote_ExpiryUnknown");
    }

    /// <summary>은퇴한 도구의 파일이 남아 있으면 그렇다고만 적는다. 그 파일의 토큰 정보를 이 도구 것처럼 얹지 않는다.</summary>
    [Fact]
    public void A_leftover_gemini_login_is_labelled_as_unrelated_and_never_supplies_token_facts()
    {
        var extras = AntigravityAuthReader.Read(hasCredential: true, Settings, hasLegacyGeminiLogin: true).Extras;
        var joined = string.Join("\n", extras.Select(note => $"{note.Key}={note.Argument}"));

        extras.Should().Contain(note => note.Key == "AuthNote_LegacyGeminiLogin");
        joined.Should().NotContain("refresh_token").And.NotContain("access_token").And.NotContain("scope");
    }

    [Fact]
    public void An_api_key_in_settings_is_reported_as_the_auth_method()
    {
        var extras = AntigravityAuthReader.Read(
            hasCredential: true, """{"apiKey":"DUMMY"}""", hasLegacyGeminiLogin: false).Extras;
        var joined = string.Join("\n", extras.Select(note => $"{note.Key}={note.Argument}"));

        extras.Should().Contain(note => note.Key == "AuthNote_AuthApiKey");
        joined.Should().NotContain("DUMMY", because: "값은 어떤 필드에도 담지 않는다");
    }

    [Fact]
    public void Without_a_settings_file_the_extras_say_so()
    {
        AntigravityAuthReader.Read(hasCredential: true, null, hasLegacyGeminiLogin: false).Extras
            .Should().Contain(note => note.Key == "AuthNote_NoSettingsFile");
    }
}

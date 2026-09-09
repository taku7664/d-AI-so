using Daiso.Core;
using Daiso.Providers.Antigravity;

namespace Daiso.Providers.Tests.Antigravity;

/// <summary>ARCHITECTURE §4.5 인증. refresh_token이 있으면 만료 개념이 없고, 없으면 access_token의 expiry_date로 판정한다.</summary>
public sealed class AntigravityAuthTests
{
    private static string OauthJson => Fixtures.ReadGemini("oauth_creds.json");

    private static string NoRefreshJson => Fixtures.ReadGemini("oauth_creds-norefresh.json");

    private static string AccountsJson => Fixtures.ReadGemini("google_accounts.json");

    /// <summary>fixture의 expiry_date 1764547200000 = 2025-12-01T00:00:00Z.</summary>
    private static readonly DateTimeOffset AccessExpiry = new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_missing_file_is_Missing()
    {
        AntigravityAuthReader.Read(null, AccountsJson, null, Fixtures.Now).State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public void A_refresh_token_means_logged_in_without_expiry()
    {
        var status = AntigravityAuthReader.Read(OauthJson, AccountsJson, null, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.SessionExpiresAt.Should().BeNull();
        status.Email.Should().Be("fixture@example.com");
        status.AccountLabel.Should().Be("Google");
    }

    [Fact]
    public void Without_a_refresh_token_the_access_token_expiry_decides()
    {
        AntigravityAuthReader.Read(NoRefreshJson, null, null, AccessExpiry.AddDays(-30)).State.Should().Be(AuthState.LoggedIn);
        AntigravityAuthReader.Read(NoRefreshJson, null, null, AccessExpiry.AddDays(-2)).State.Should().Be(AuthState.ExpiringSoon);
        AntigravityAuthReader.Read(NoRefreshJson, null, null, AccessExpiry.AddSeconds(1)).State.Should().Be(AuthState.Expired);
    }

    [Fact]
    public void Extras_never_contain_token_values()
    {
        var status = AntigravityAuthReader.Read(OauthJson, AccountsJson, null, Fixtures.Now);

        var joined = string.Join("\n", status.Extras);
        joined.Should().NotContain("FAKE-ACCESS").And.NotContain("FAKE-REFRESH").And.NotContain("FAKE-ID");
        joined.Should().Contain("refresh_token: 있음").And.Contain("scope: 3개");
    }

    [Fact]
    public void A_missing_accounts_file_leaves_the_email_empty()
    {
        var status = AntigravityAuthReader.Read(OauthJson, null, null, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.Email.Should().BeNull();
    }

    /// <summary>
    /// 옛 로그인 파일이 없어도 `antigravity-cli/settings.json` 이 있으면 설정한 적이 있다는 뜻이라 없음으로 보지 않는다.
    /// 만료는 자격 증명 관리자에 있어 알 수 없으므로 비운다.
    /// </summary>
    [Fact]
    public void Only_the_antigravity_settings_file_still_counts_as_configured()
    {
        var status = AntigravityAuthReader.Read(null, null, "{}", Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.SessionExpiresAt.Should().BeNull();
        status.AccountLabel.Should().Be("Google");
    }

    /// <summary>만료를 못 보여 주는 이유가 화면에 남아야 한다 — 아무 말이 없으면 앱이 못 읽는 것인지 로그인이 안 된 것인지 모른다.</summary>
    [Fact]
    public void Extras_say_where_the_login_is_kept()
    {
        var status = AntigravityAuthReader.Read(OauthJson, AccountsJson, null, Fixtures.Now);

        string.Join("\n", status.Extras).Should().Contain("자격 증명 관리자");
    }
}

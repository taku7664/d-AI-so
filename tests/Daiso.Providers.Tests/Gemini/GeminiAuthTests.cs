using Daiso.Core;
using Daiso.Providers.Gemini;

namespace Daiso.Providers.Tests.Gemini;

/// <summary>ARCHITECTURE §4.5 인증. refresh_token이 있으면 만료 개념이 없고, 없으면 access_token의 expiry_date로 판정한다.</summary>
public sealed class GeminiAuthTests
{
    private static string OauthJson => Fixtures.ReadGemini("oauth_creds.json");

    private static string NoRefreshJson => Fixtures.ReadGemini("oauth_creds-norefresh.json");

    private static string AccountsJson => Fixtures.ReadGemini("google_accounts.json");

    /// <summary>fixture의 expiry_date 1764547200000 = 2025-12-01T00:00:00Z.</summary>
    private static readonly DateTimeOffset AccessExpiry = new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_missing_file_is_Missing()
    {
        GeminiAuthReader.Read(null, AccountsJson, Fixtures.Now).State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public void A_refresh_token_means_logged_in_without_expiry()
    {
        var status = GeminiAuthReader.Read(OauthJson, AccountsJson, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.SessionExpiresAt.Should().BeNull();
        status.Email.Should().Be("fixture@example.com");
        status.AccountLabel.Should().Be("Google");
    }

    [Fact]
    public void Without_a_refresh_token_the_access_token_expiry_decides()
    {
        GeminiAuthReader.Read(NoRefreshJson, null, AccessExpiry.AddDays(-30)).State.Should().Be(AuthState.LoggedIn);
        GeminiAuthReader.Read(NoRefreshJson, null, AccessExpiry.AddDays(-2)).State.Should().Be(AuthState.ExpiringSoon);
        GeminiAuthReader.Read(NoRefreshJson, null, AccessExpiry.AddSeconds(1)).State.Should().Be(AuthState.Expired);
    }

    [Fact]
    public void Extras_never_contain_token_values()
    {
        var status = GeminiAuthReader.Read(OauthJson, AccountsJson, Fixtures.Now);

        var joined = string.Join("\n", status.Extras);
        joined.Should().NotContain("FAKE-ACCESS").And.NotContain("FAKE-REFRESH").And.NotContain("FAKE-ID");
        joined.Should().Contain("refresh_token: 있음").And.Contain("scope: 3개");
    }

    [Fact]
    public void A_missing_accounts_file_leaves_the_email_empty()
    {
        var status = GeminiAuthReader.Read(OauthJson, null, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.Email.Should().BeNull();
    }
}

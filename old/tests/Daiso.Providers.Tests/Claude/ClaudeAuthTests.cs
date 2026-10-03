using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Claude;

namespace Daiso.Providers.Tests.Claude;

/// <summary>ARCHITECTURE §4.1 인증. 상태는 refreshTokenExpiresAt으로만 판정한다.</summary>
public sealed class ClaudeAuthTests
{
    private static string Credentials => Fixtures.ReadClaude("credentials.json");

    private static string ClaudeJson => Fixtures.ReadClaude("claude.json");

    [Fact]
    public void A_missing_credentials_file_is_Missing()
    {
        var status = ClaudeAuthReader.Read(null, ClaudeJson, Fixtures.Now);

        status.State.Should().Be(AuthState.Missing);
        status.SessionExpiresAt.Should().BeNull();
        status.Extras.Should().BeEmpty();
    }

    [Fact]
    public void A_far_expiry_is_LoggedIn()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.SessionExpiresAt.Should().Be(Fixtures.Expiry);
    }

    [Fact]
    public void An_expiry_within_seven_days_is_ExpiringSoon()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Expiry.AddDays(-3));

        status.State.Should().Be(AuthState.ExpiringSoon);
    }

    [Fact]
    public void A_past_expiry_is_Expired()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Expiry.AddDays(1));

        status.State.Should().Be(AuthState.Expired);
    }

    [Fact]
    public void The_short_lived_access_token_expiry_is_ignored()
    {
        // fixture의 expiresAt은 2026-01-01로 이미 지났지만 상태는 LoggedIn이어야 한다.
        ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now)
            .State.Should().Be(AuthState.LoggedIn);
    }

    [Fact]
    public void The_account_label_and_email_come_from_claude_json()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now);

        // 구독 등급은 이름 줄에 섞지 않는다. 카드에 요금제 자리가 따로 있다
        status.AccountLabel.Should().Be("Fixture User · Fixture Org");
        status.Email.Should().Be("fixture@example.test");
        status.Plan.Should().Be("max");
    }

    [Fact]
    public void Without_claude_json_the_account_fields_are_null()
    {
        var status = ClaudeAuthReader.Read(Credentials, null, Fixtures.Now);

        status.AccountLabel.Should().BeNull();
        status.Email.Should().BeNull();
    }

    [Fact]
    public void Extras_list_the_tier_scopes_and_mcp_connector_names()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now);

        status.Extras.Should().NotContain(note => note.Key == "AuthNote_Subscription",
            because: "구독은 카드의 요금제 줄이 맡는다. 부가 정보에서 한 번 더 말하지 않는다");
        status.Extras.Should().Contain(new AuthNote("AuthNote_RateLimitTier", "tier4"));
        status.Extras.Should().Contain(new AuthNote("AuthNote_Scope", "user:inference"));
        status.Extras.Should().Contain(new AuthNote("AuthNote_Mcp", "fixture-connector"));
    }

    [Fact]
    public void Extras_never_contain_the_mcp_url_or_token()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now);

        status.Extras.Should().NotContain(note =>
            note.Argument != null && note.Argument.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void Serializing_the_status_never_leaks_a_token()
    {
        var status = ClaudeAuthReader.Read(Credentials, ClaudeJson, Fixtures.Now);

        JsonSerializer.Serialize(status).Should().NotContain("DUMMY_TOKEN");
    }
}

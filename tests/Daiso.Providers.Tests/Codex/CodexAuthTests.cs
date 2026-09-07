using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Codex;

namespace Daiso.Providers.Tests.Codex;

/// <summary>ARCHITECTURE §4.2 인증. 만료는 access_token JWT의 exp로 판정한다.</summary>
public sealed class CodexAuthTests
{
    private static string AuthJson => Fixtures.ReadCodex("auth.json");

    private static string ApiKeyAuthJson => Fixtures.ReadCodex("auth-apikey.json");

    [Fact]
    public void A_missing_auth_file_is_Missing()
    {
        CodexAuthReader.Read(null, Fixtures.Now).State.Should().Be(AuthState.Missing);
    }

    [Fact]
    public void A_far_jwt_expiry_is_LoggedIn()
    {
        var status = CodexAuthReader.Read(AuthJson, Fixtures.Now);

        status.State.Should().Be(AuthState.LoggedIn);
        status.SessionExpiresAt.Should().Be(Fixtures.Expiry);
        status.AccountLabel.Should().Be("chatgpt");
    }

    [Fact]
    public void A_jwt_expiry_within_seven_days_is_ExpiringSoon()
    {
        CodexAuthReader.Read(AuthJson, Fixtures.Expiry.AddDays(-2))
            .State.Should().Be(AuthState.ExpiringSoon);
    }

    [Fact]
    public void A_past_jwt_expiry_is_Expired()
    {
        CodexAuthReader.Read(AuthJson, Fixtures.Expiry.AddSeconds(1))
            .State.Should().Be(AuthState.Expired);
    }

    [Fact]
    public void An_api_key_mode_has_no_expiry()
    {
        var status = CodexAuthReader.Read(ApiKeyAuthJson, Fixtures.Now);

        status.SessionExpiresAt.Should().BeNull();
        status.State.Should().Be(AuthState.LoggedIn);
        status.Extras.Should().Contain("OPENAI_API_KEY: 설정됨");
    }

    [Fact]
    public void An_undecodable_token_leaves_the_expiry_null()
    {
        const string Json = """
            {"auth_mode": "chatgpt", "tokens": {"access_token": "not-a-jwt"}}
            """;

        var status = CodexAuthReader.Read(Json, Fixtures.Now);

        status.SessionExpiresAt.Should().BeNull();
        status.State.Should().Be(AuthState.LoggedIn);
    }

    [Fact]
    public void Extras_report_the_mode_and_whether_an_api_key_is_set()
    {
        var status = CodexAuthReader.Read(AuthJson, Fixtures.Now);

        status.Extras.Should().Contain("auth_mode: chatgpt");
        status.Extras.Should().Contain("OPENAI_API_KEY: 없음");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Serializing_the_status_never_leaks_a_token(bool apiKeyMode)
    {
        var status = CodexAuthReader.Read(apiKeyMode ? ApiKeyAuthJson : AuthJson, Fixtures.Now);

        JsonSerializer.Serialize(status).Should().NotContain("DUMMY_TOKEN");
    }
}

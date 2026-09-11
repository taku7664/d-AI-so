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
        // 계정 이름 자리에는 계정 이름만 들어간다. 인증 방식(`chatgpt`)도 요금제도 아니다
        status.AccountLabel.Should().BeNull(because: "id 토큰에 사람 이름이 없다");
        status.Email.Should().Be("fixture@example.com");
        status.Plan.Should().Be("plus");
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
        status.Extras.Should().Contain(note => note.Key == "AuthNote_ApiKeySet");
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

        // 파일에 적힌 `chatgpt` 를 그대로 옮기지 않고 사람이 읽는 문구 키로 바꾼다
        status.Extras.Should().Contain(new AuthNote("AuthNote_AuthChatGpt"));
        status.Extras.Should().Contain(note => note.Key == "AuthNote_ApiKeyMissing");
        status.Extras.Should().NotContain(note => note.Key == "AuthNote_AccountId",
            because: "account_id 는 사람이 쓸 데가 없다");
    }

    [Fact]
    public void An_unknown_auth_mode_is_shown_as_written()
    {
        const string Json = """
            {"auth_mode": "something-new", "tokens": {}}
            """;

        CodexAuthReader.Read(Json, Fixtures.Now).Extras
            .Should().Contain(new AuthNote("AuthNote_AuthMode", "something-new"),
                because: "모르는 방식이면 값을 그대로 보여 준다 — 침묵하면 무엇으로 로그인했는지 알 수 없다");
    }

    [Fact]
    public void An_id_token_that_says_nothing_leaves_the_account_empty()
    {
        const string Json = """
            {"auth_mode": "chatgpt", "tokens": {"id_token": "not-a-jwt"}}
            """;

        var status = CodexAuthReader.Read(Json, Fixtures.Now);

        status.AccountLabel.Should().BeNull(because: "없는 값을 다른 값으로 메우지 않는다");
        status.Email.Should().BeNull();
        status.Plan.Should().BeNull();
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

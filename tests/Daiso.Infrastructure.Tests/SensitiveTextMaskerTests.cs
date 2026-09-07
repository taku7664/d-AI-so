namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §7.1 — 로그에 토큰이 남지 않아야 한다.</summary>
public sealed class SensitiveTextMaskerTests
{
    [Fact]
    public void An_api_key_prefix_is_masked()
    {
        var masked = SensitiveTextMasker.MaskSensitive("키는 sk-ant-oat01-ABCDEFGHIJKLMNOP 이다");

        masked.Should().NotContain("sk-ant");
        masked.Should().Contain(SensitiveTextMasker.Redacted);
    }

    [Fact]
    public void A_jwt_is_masked()
    {
        const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJleHAiOjE3ODg3OTk4Njd9.SflKxwRJSMeKKF2QT4fwpMeJf36P";

        var masked = SensitiveTextMasker.MaskSensitive($"access_token={Jwt}");

        masked.Should().NotContain("eyJ");
    }

    [Fact]
    public void A_bearer_header_is_masked()
    {
        var masked = SensitiveTextMasker.MaskSensitive("Authorization: Bearer ABCDEFGHIJKLMNOPQRSTUVWX");

        masked.Should().NotContain("ABCDEFGHIJKLMNOPQRSTUVWX");
        masked.Should().Contain("Bearer");
    }

    [Theory]
    [InlineData("accessToken\": \"ABCDEFGHIJKLMNOP\"")]
    [InlineData("refresh_token=ABCDEFGHIJKLMNOP")]
    [InlineData("OPENAI_API_KEY: ABCDEFGHIJKLMNOP")]
    [InlineData("apiKey=ABCDEFGHIJKLMNOP")]
    public void A_named_secret_assignment_is_masked(string line)
    {
        var masked = SensitiveTextMasker.MaskSensitive(line);

        masked.Should().NotContain("ABCDEFGHIJKLMNOP");
        masked.Should().Contain(SensitiveTextMasker.Redacted);
    }

    [Fact]
    public void A_long_opaque_blob_is_masked()
    {
        var blob = new string('A', 48);

        SensitiveTextMasker.MaskSensitive($"값 {blob} 끝").Should().NotContain(blob);
    }

    [Fact]
    public void Ordinary_text_and_paths_survive()
    {
        const string Text = @"C:\Users\Fixture\Documents\GitHub\d-AI-so 에서 세션 248건을 읽었다";

        SensitiveTextMasker.MaskSensitive(Text).Should().Be(Text);
    }

    [Fact]
    public void A_stack_trace_stays_readable()
    {
        const string Trace =
            "System.IO.IOException: 파일을 찾을 수 없다\n"
            + "   at Daiso.Infrastructure.RuleFileService.Load(String path)\n"
            + "   at Daiso.App.ViewModels.RuleMakerViewModel.Open(String path)";

        var masked = SensitiveTextMasker.MaskSensitive(Trace);

        masked.Should().Contain("RuleFileService.Load");
        masked.Should().Contain("RuleMakerViewModel.Open");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Blank_input_comes_back_empty(string? text)
    {
        SensitiveTextMasker.MaskSensitive(text).Should().BeEmpty();
    }

    [Fact]
    public void A_session_id_is_short_enough_to_survive()
    {
        // 세션 ID는 로그에서 쓸모가 있으므로 가려지지 않아야 한다.
        const string SessionId = "5c6502d9-5146-41d2-9c88-1673637a697a";

        SensitiveTextMasker.MaskSensitive($"세션 {SessionId} 처리 중").Should().Contain(SessionId);
    }
}

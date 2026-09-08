using Daiso.Infrastructure.Pty;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.3 — 방 프로세스 환경. 중첩 표식은 걷어내고 사용자 변수는 남긴다.</summary>
public sealed class PtyEnvironmentTests
{
    [Fact]
    public void Nested_claude_code_markers_are_removed_and_user_variables_stay()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = @"C:\bin",
            ["USERPROFILE"] = @"C:\Users\x",
            ["CLAUDECODE"] = "1",
            ["CLAUDE_CODE_ENTRYPOINT"] = "cli",
            ["CLAUDE_CODE_SESSION_ID"] = "abc",
            ["CLAUDE_PID"] = "1",
            ["ANTHROPIC_BASE_URL"] = "http://proxy",
            ["MY_TOOL_SETTING"] = "keep",
        };

        var result = PtyEnvironment.Sanitize(source);

        result.Keys.Should().NotContain(key => key.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase));
        result.Should().NotContainKey("ANTHROPIC_BASE_URL", because: "중첩 세션이 프록시로 돌린 값이다");
        result["PATH"].Should().Be(@"C:\bin");
        result["MY_TOOL_SETTING"].Should().Be("keep");
        result["TERM"].Should().Be("xterm-256color");
    }

    [Fact]
    public void Outside_a_nested_session_the_users_anthropic_settings_are_kept()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANTHROPIC_BASE_URL"] = "http://my-gateway",
        };

        PtyEnvironment.Sanitize(source).Should().ContainKey("ANTHROPIC_BASE_URL");
    }

    [Fact]
    public void Environment_block_is_double_null_terminated()
    {
        var block = PtyEnvironment.ToBlock(new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" });

        block.Should().Be("A=1\0B=2\0\0");
    }
}

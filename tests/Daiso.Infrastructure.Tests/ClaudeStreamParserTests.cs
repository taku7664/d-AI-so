using Daiso.Core.Chat;
using Daiso.Infrastructure.Chat;

namespace Daiso.Infrastructure.Tests;

/// <summary>FEATURE_PLAN B 모드 — Claude stream-json 한 줄을 챗봇 사건으로 옮긴다.</summary>
public sealed class ClaudeStreamParserTests
{
    [Fact]
    public void Init_becomes_started()
    {
        var e = ClaudeStreamParser.Parse("""{"type":"system","subtype":"init","session_id":"abc","model":"claude-x"}""");
        e.Should().ContainSingle().Which.Should().BeOfType<ChatEvent.Started>()
            .Which.Should().BeEquivalentTo(new ChatEvent.Started("abc", "claude-x"));
    }

    [Fact]
    public void Text_delta_becomes_assistant_delta()
    {
        var e = ClaudeStreamParser.Parse("""{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"안녕"}}}""");
        e.Should().ContainSingle().Which.Should().Be(new ChatEvent.AssistantDelta("안녕"));
    }

    [Fact]
    public void Assistant_message_yields_text_and_tool_use()
    {
        var e = ClaudeStreamParser.Parse("""{"type":"assistant","message":{"content":[{"type":"text","text":"할게요"},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls"}}]}}""");
        e.Should().HaveCount(2);
        e[0].Should().Be(new ChatEvent.AssistantMessage("할게요"));
        var tool = e[1].Should().BeOfType<ChatEvent.ToolUse>().Subject;
        tool.Id.Should().Be("t1");
        tool.Name.Should().Be("Bash");
        tool.Summary.Should().Contain("ls");
    }

    [Fact]
    public void Can_use_tool_control_request_becomes_permission_request()
    {
        var e = ClaudeStreamParser.Parse("""{"type":"control_request","request_id":"r1","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"rm x"}}}""");
        var p = e.Should().ContainSingle().Which.Should().BeOfType<ChatEvent.PermissionRequest>().Subject;
        p.RequestId.Should().Be("r1");
        p.ToolName.Should().Be("Bash");
        p.Summary.Should().Contain("rm x");
    }

    [Fact]
    public void Tool_result_from_user_message_becomes_tool_result()
    {
        var e = ClaudeStreamParser.Parse("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","is_error":false,"content":[{"type":"text","text":"파일 목록"}]}]}}""");
        var r = e.Should().ContainSingle().Which.Should().BeOfType<ChatEvent.ToolResult>().Subject;
        r.Id.Should().Be("t1");
        r.Summary.Should().Be("파일 목록");
        r.IsError.Should().BeFalse();
    }

    [Fact]
    public void Result_becomes_turn_ended()
    {
        ClaudeStreamParser.Parse("""{"type":"result","subtype":"success","result":"끝"}""")
            .Should().ContainSingle().Which.Should().Be(new ChatEvent.TurnEnded(false, "끝"));
        ClaudeStreamParser.Parse("""{"type":"result","subtype":"error_max_turns"}""")
            .Should().ContainSingle().Which.Should().BeOfType<ChatEvent.TurnEnded>()
            .Which.IsError.Should().BeTrue();
    }

    [Fact]
    public void Junk_is_ignored_not_thrown()
    {
        ClaudeStreamParser.Parse("not json").Should().ContainSingle().Which.Should().BeOfType<ChatEvent.Ignored>();
        ClaudeStreamParser.Parse("""{"type":"unknown"}""").Should().ContainSingle().Which.Should().BeOfType<ChatEvent.Ignored>();
        ClaudeStreamParser.Parse("").Should().ContainSingle().Which.Should().BeOfType<ChatEvent.Ignored>();
    }
}

using System.Text;
using System.Text.Json;
using Daiso.Core.Chat;

namespace Daiso.Infrastructure.Chat;

/// <summary>
/// Claude Code의 `--output-format stream-json --include-partial-messages` 한 줄(JSON)을 <see cref="ChatEvent"/>로 옮긴다. (FEATURE_PLAN B 모드)
///
/// 줄의 <c>type</c>으로 가른다:
/// | type | 뜻 | 올리는 사건 |
/// |---|---|---|
/// | system (subtype=init) | 세션 시작 | Started |
/// | stream_event | Anthropic 스트리밍 이벤트 래핑. content_block_delta의 text_delta가 글자 조각 | AssistantDelta |
/// | assistant | 어시스턴트 메시지 한 덩어리(text·tool_use 블록) | AssistantMessage / ToolUse |
/// | user | 도구 결과가 담겨 온다 | ToolResult |
/// | control_request (can_use_tool) | 도구 승인 요청 | PermissionRequest |
/// | result | 턴 끝 | TurnEnded |
///
/// 순수 함수라 테스트로 고정한다. 토큰·비밀은 요약에 담지 않는다.
/// </summary>
public static class ClaudeStreamParser
{
    private const int SummaryLimit = 400;

    /// <summary>한 줄을 사건들로. 파싱 못 하면 Ignored 하나.</summary>
    public static IReadOnlyList<ChatEvent> Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [new ChatEvent.Ignored("빈 줄")];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return [new ChatEvent.Ignored("JSON 아님")];
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement))
            {
                return [new ChatEvent.Ignored("type 없음")];
            }

            return typeElement.GetString() switch
            {
                "system" => System(root),
                "stream_event" => StreamEvent(root),
                "assistant" => AssistantMessage(root),
                "user" => UserMessage(root),
                "control_request" => ControlRequest(root),
                "result" => [Result(root)],
                _ => [new ChatEvent.Ignored("모르는 type")],
            };
        }
    }

    private static IReadOnlyList<ChatEvent> System(JsonElement root)
    {
        if (Str(root, "subtype") != "init")
        {
            return [new ChatEvent.Ignored("system 그 밖")];
        }

        return [new ChatEvent.Started(Str(root, "session_id"), Str(root, "model"))];
    }

    /// <summary>stream_event: Anthropic 스트리밍 이벤트를 감싼 것. text_delta만 조각으로 올린다.</summary>
    private static IReadOnlyList<ChatEvent> StreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
        {
            return [new ChatEvent.Ignored("event 없음")];
        }

        if (Str(ev, "type") != "content_block_delta"
            || !ev.TryGetProperty("delta", out var delta)
            || Str(delta, "type") != "text_delta")
        {
            return [new ChatEvent.Ignored("델타 아님")];
        }

        var text = Str(delta, "text") ?? string.Empty;
        return text.Length == 0 ? [new ChatEvent.Ignored("빈 델타")] : [new ChatEvent.AssistantDelta(text)];
    }

    /// <summary>assistant 메시지: content 블록을 편다. text는 (스트리밍을 못 받은 경우의) 대체, tool_use는 카드.</summary>
    private static IReadOnlyList<ChatEvent> AssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return [new ChatEvent.Ignored("assistant content 없음")];
        }

        var events = new List<ChatEvent>();

        foreach (var block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text" when Str(block, "text") is { Length: > 0 } text:
                    events.Add(new ChatEvent.AssistantMessage(text));
                    break;

                case "tool_use":
                    events.Add(new ChatEvent.ToolUse(
                        Str(block, "id") ?? string.Empty,
                        Str(block, "name") ?? "tool",
                        ToolSummary(Str(block, "name"), block.TryGetProperty("input", out var input) ? input : default)));
                    break;

                default:
                    break;
            }
        }

        return events.Count > 0 ? events : [new ChatEvent.Ignored("빈 assistant")];
    }

    /// <summary>user 메시지: 도구 결과(tool_result 블록)가 담겨 온다.</summary>
    private static IReadOnlyList<ChatEvent> UserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return [new ChatEvent.Ignored("user content 없음")];
        }

        var events = new List<ChatEvent>();

        foreach (var block in content.EnumerateArray())
        {
            if (Str(block, "type") != "tool_result")
            {
                continue;
            }

            events.Add(new ChatEvent.ToolResult(
                Str(block, "tool_use_id") ?? string.Empty,
                Shorten(ContentText(block.TryGetProperty("content", out var c) ? c : default)),
                block.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True));
        }

        return events.Count > 0 ? events : [new ChatEvent.Ignored("도구 결과 아님")];
    }

    /// <summary>control_request: can_use_tool이면 승인 카드로.</summary>
    private static IReadOnlyList<ChatEvent> ControlRequest(JsonElement root)
    {
        if (!root.TryGetProperty("request", out var request) || Str(request, "subtype") != "can_use_tool")
        {
            return [new ChatEvent.Ignored("제어 요청 그 밖")];
        }

        var name = Str(request, "tool_name") ?? "tool";
        return [new ChatEvent.PermissionRequest(
            Str(root, "request_id") ?? string.Empty,
            name,
            ToolSummary(name, request.TryGetProperty("input", out var input) ? input : default))];
    }

    private static ChatEvent Result(JsonElement root)
    {
        var isError = Str(root, "subtype") is { } subtype && subtype != "success";
        return new ChatEvent.TurnEnded(isError, Str(root, "result"));
    }

    /// <summary>도구 한 건을 한 줄로. 인자 JSON은 짧게 자르고 비밀이 들어갈 자리는 두지 않는다.</summary>
    private static string ToolSummary(string? name, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object && input.ValueKind != JsonValueKind.Array)
        {
            return name ?? "tool";
        }

        var raw = input.GetRawText();
        return Shorten($"{name} {raw}");
    }

    private static string ContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (Str(part, "text") is { Length: > 0 } text)
            {
                builder.Append(text);
            }
        }

        return builder.ToString();
    }

    private static string Shorten(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= SummaryLimit ? trimmed : trimmed[..SummaryLimit] + " …";
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

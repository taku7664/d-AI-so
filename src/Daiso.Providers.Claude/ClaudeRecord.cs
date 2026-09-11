using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Claude;

/// <summary>세션 jsonl 한 줄에서 뽑아낸 것. (ARCHITECTURE §4.1 레코드 필드)</summary>
internal sealed record ClaudeRecord(
    string? SessionId,
    string? Cwd,
    string? Version,
    DateTimeOffset? Timestamp,
    bool IsSidechain,
    IReadOnlyList<SessionMessage> Messages,
    TokenUsage? Usage);

/// <summary>jsonl 한 줄을 <see cref="ClaudeRecord"/>로 바꾼다.</summary>
internal static class ClaudeRecordParser
{
    /// <summary>usage 합산과 모델별 집계에서 빼는 모델 이름.</summary>
    internal const string SyntheticModel = "<synthetic>";

    private static readonly IReadOnlyList<SessionMessage> NoMessages = [];

    /// <summary>파싱할 수 없는 줄이면 null.</summary>
    internal static ClaudeRecord? Parse(string line)
    {
        using var document = JsonHelpers.TryParseLine(line);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = root.Prop("type").Text();
        var at = root.Prop("timestamp").Timestamp();
        var isSidechain = root.Prop("isSidechain").Boolean() ?? false;
        var isMeta = root.Prop("isMeta").Boolean() ?? false;

        var record = new ClaudeRecord(
            root.Prop("sessionId").Text(),
            root.Prop("cwd").Text(),
            root.Prop("version").Text(),
            at,
            isSidechain,
            NoMessages,
            null);

        return type switch
        {
            "user" when !isMeta => record with { Messages = UserMessages(root, at ?? default, isSidechain) },
            "user" => record,
            "assistant" => Assistant(root, record, at ?? default, isSidechain),
            "system" => record with { Messages = System(root, at ?? default, isSidechain) },
            _ => record,
        };
    }

    /// <summary>
    /// 문자열 content → User. text 블록만 있는 배열 → User.
    /// tool_result 블록이 있으면 Tool로 분류한다.
    /// </summary>
    private static IReadOnlyList<SessionMessage> UserMessages(
        JsonElement root,
        DateTimeOffset at,
        bool isSidechain)
    {
        var content = root.Path("message", "content");

        if (content is { ValueKind: JsonValueKind.String })
        {
            var text = content.Text();
            return text is null ? NoMessages : [new SessionMessage(at, MessageRole.User, text, isSidechain)];
        }

        if (content is not { ValueKind: JsonValueKind.Array })
        {
            return NoMessages;
        }

        var hasToolResult = false;
        var builder = new StringBuilder();

        foreach (var block in content.Items())
        {
            switch (block.Prop("type").Text())
            {
                case "tool_result":
                    hasToolResult = true;
                    break;
                case "text" when block.Prop("text").Text() is { } text:
                    Append(builder, text);
                    break;
                default:
                    break;
            }
        }

        if (hasToolResult)
        {
            return [new SessionMessage(at, MessageRole.Tool, ToolResultText(root), isSidechain)];
        }

        return builder.Length == 0
            ? NoMessages
            : [new SessionMessage(at, MessageRole.User, builder.ToString(), isSidechain)];
    }

    private static string ToolResultText(JsonElement root) =>
        root.Prop("toolUseResult") is { } result && result.ValueKind != JsonValueKind.Null
            ? Truncate(result.ToString())
            : "tool_result";

    /// <summary>text 블록은 Assistant, tool_use 블록은 Tool. usage는 synthetic 모델을 뺀다.</summary>
    private static ClaudeRecord Assistant(
        JsonElement root,
        ClaudeRecord record,
        DateTimeOffset at,
        bool isSidechain)
    {
        var message = root.Prop("message");
        var model = message?.Prop("model").Text();
        var isSynthetic = string.Equals(model, SyntheticModel, StringComparison.Ordinal);

        var messages = new List<SessionMessage>(2);
        var text = new StringBuilder();
        var tools = new List<string>();

        foreach (var block in message?.Prop("content").Items() ?? [])
        {
            switch (block.Prop("type").Text())
            {
                case "text" when block.Prop("text").Text() is { } chunk:
                    Append(text, chunk);
                    break;
                case "tool_use":
                    tools.Add(ToolUseText(block));
                    break;
                default:
                    break;
            }
        }

        if (text.Length > 0)
        {
            messages.Add(new SessionMessage(at, MessageRole.Assistant, text.ToString(), isSidechain));
        }

        foreach (var tool in tools)
        {
            messages.Add(new SessionMessage(at, MessageRole.Tool, tool, isSidechain));
        }

        return record with
        {
            Messages = messages,
            Usage = isSynthetic ? null : Usage(message?.Prop("usage"), model),
        };
    }

    private static string ToolUseText(JsonElement block)
    {
        var name = block.Prop("name").Text() ?? "tool_use";
        var input = block.Prop("input");
        return input is null ? name : $"{name} {Truncate(input.Value.ToString())}";
    }

    private static IReadOnlyList<SessionMessage> System(
        JsonElement root,
        DateTimeOffset at,
        bool isSidechain)
    {
        var text = root.Prop("content").Text() ?? root.Path("message", "content").Text();
        return text is null
            ? NoMessages
            : [new SessionMessage(at, MessageRole.System, text, isSidechain)];
    }

    private static TokenUsage? Usage(JsonElement? usage, string? model)
    {
        if (usage is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        return new TokenUsage(
            usage.Prop("input_tokens").NumberOrZero(),
            usage.Prop("output_tokens").NumberOrZero(),
            usage.Prop("cache_creation_input_tokens").NumberOrZero(),
            usage.Prop("cache_read_input_tokens").NumberOrZero(),
            model);
    }

    private static void Append(StringBuilder builder, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(text);
    }

    /// <summary>도구 결과는 인덱스에 넣지 않으므로 표시용으로만 짧게 남긴다.</summary>
    private static string Truncate(string text) =>
        text.Length <= 500 ? text : TextCut.Head(text, 500) + "…";
}

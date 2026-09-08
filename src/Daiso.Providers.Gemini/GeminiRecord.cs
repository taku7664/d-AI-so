using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Gemini;

/// <summary>Gemini 대화 기록 jsonl 한 줄에서 뽑아낸 것. (ARCHITECTURE §4.5)</summary>
internal sealed record GeminiRecord(
    string? SessionId,
    string? ProjectHash,
    DateTimeOffset? StartTime,
    DateTimeOffset? Timestamp,
    string? Model,
    TokenUsage? Usage,
    IReadOnlyList<SessionMessage> Messages);

/// <summary>
/// `~/.gemini/tmp/{project}/chats/session-*.jsonl` 의 줄을 <see cref="GeminiRecord"/>로 바꾼다.
///
/// - 헤더 줄: <c>{sessionId, projectHash, startTime, lastUpdated, kind}</c>. 메시지가 없다
/// - 메시지 줄: <c>{id, timestamp, type, content, tokens?, model?, toolCalls?}</c>
///   · <c>type == "user"</c> → User, <c>"gemini"</c> → Assistant, <c>"info" | "warning" | "error"</c> → System
///   · <c>content</c>는 문자열이거나 <c>[{text}]</c> 조각 배열이다
///   · <c>toolCalls[]</c>는 각각 Tool 메시지 하나로 낸다(이름 + 인자 앞부분). 결과 본문은 넣지 않는다
///   · <c>tokens</c>는 메시지마다 붙는 그 응답의 사용량이라 날짜별로 더한다
/// </summary>
internal static class GeminiRecordParser
{
    private const int ToolArgsPreview = 300;
    private static readonly IReadOnlyList<SessionMessage> NoMessages = [];

    internal static GeminiRecord? Parse(string line)
    {
        using var document = JsonHelpers.TryParseLine(line);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        var type = root.Prop("type").Text();

        // 헤더 줄에는 type이 없고 sessionId·projectHash가 있다
        if (type is null)
        {
            if (root.Prop("sessionId").Text() is null)
            {
                return null;
            }

            return new GeminiRecord(
                root.Prop("sessionId").Text(),
                root.Prop("projectHash").Text(),
                root.Prop("startTime").Timestamp(),
                null,
                null,
                null,
                NoMessages);
        }

        var at = root.Prop("timestamp").Timestamp() ?? default;
        var messages = new List<SessionMessage>();

        switch (type)
        {
            case "user":
                Add(messages, at, MessageRole.User, ContentText(root.Prop("content")));
                break;
            case "gemini":
                Add(messages, at, MessageRole.Assistant, ContentText(root.Prop("content")));

                foreach (var call in root.Prop("toolCalls").Items())
                {
                    Add(messages, call.Prop("timestamp").Timestamp() ?? at, MessageRole.Tool, ToolCallText(call));
                }

                break;
            case "info":
            case "warning":
            case "error":
                Add(messages, at, MessageRole.System, ContentText(root.Prop("content")));
                break;
            default:
                break;
        }

        return new GeminiRecord(
            null,
            null,
            null,
            at == default ? null : at,
            root.Prop("model").Text(),
            Usage(root.Prop("tokens"), root.Prop("model").Text()),
            messages);
    }

    private static void Add(List<SessionMessage> messages, DateTimeOffset at, MessageRole role, string text)
    {
        if (text.Length > 0)
        {
            messages.Add(new SessionMessage(at, role, text, IsSidechain: false));
        }
    }

    /// <summary>문자열이면 그대로, 조각 배열이면 text 조각을 줄바꿈으로 이어 붙인다.</summary>
    private static string ContentText(JsonElement? content)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (content is { ValueKind: JsonValueKind.String })
        {
            return content.Text()?.Trim() ?? string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var part in content.Items())
        {
            if (part.Prop("text").Text() is { Length: > 0 } text)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(text);
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>도구 호출 한 건을 한 줄로. 결과 본문은 크고 인덱스를 더럽혀 넣지 않는다.</summary>
    private static string ToolCallText(JsonElement call)
    {
        var name = call.Prop("name").Text() ?? "tool";
        var args = call.Prop("args") is { } a ? a.GetRawText() : string.Empty;

        if (args.Length > ToolArgsPreview)
        {
            args = args[..ToolArgsPreview] + " …";
        }

        var status = call.Prop("status").Text();

        return status is null ? $"{name} {args}".Trim() : $"{name} {args} [{status}]".Trim();
    }

    /// <summary>
    /// <c>tokens: {input, output, cached, thoughts, tool, total}</c>.
    /// 도구 프롬프트 토큰은 입력 쪽, 사고 토큰은 출력 쪽으로 더한다. 캐시는 읽기만 있다.
    /// </summary>
    private static TokenUsage? Usage(JsonElement? tokens, string? model)
    {
        if (tokens is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        return new TokenUsage(
            tokens.Prop("input").NumberOrZero() + tokens.Prop("tool").NumberOrZero(),
            tokens.Prop("output").NumberOrZero() + tokens.Prop("thoughts").NumberOrZero(),
            0,
            tokens.Prop("cached").NumberOrZero(),
            model);
    }
}

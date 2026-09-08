using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Gemini;

/// <summary>대화 기록을 끝까지 리플레이한 결과. (ARCHITECTURE §4.5)</summary>
internal sealed record GeminiTranscript(
    string? SessionId,
    string? ProjectHash,
    DateTimeOffset? StartTime,
    IReadOnlyList<GeminiMessage> Messages);

/// <summary>리플레이가 끝난 뒤의 메시지 한 건. 같은 id가 다시 오면 이것이 덧씌워진 결과다.</summary>
internal sealed record GeminiMessage(
    string Id,
    DateTimeOffset At,
    string Type,
    string Text,
    string? Model,
    TokenUsage? Usage,
    IReadOnlyList<string> ToolCalls);

/// <summary>
/// `~/.gemini/tmp/{project}/chats/session-*.jsonl` 은 append-only 메시지 로그가 **아니다**. 레코드 네 가지를 순서대로 리플레이해야 최종 상태가 나온다.
/// (Gemini CLI 0.58 `chatRecordingService`의 읽기 코드와 같은 규칙)
///
/// | 레코드 | 판별 | 뜻 |
/// |---|---|---|
/// | 헤더 | `sessionId`·`projectHash` 문자열 | 세션 메타. 첫 줄 |
/// | 메시지 | `id` 문자열 | 같은 id가 이미 있으면 그 자리에서 덧씀(토큰이 나중에 붙는다). 없으면 뒤에 붙임 |
/// | `$set` | `$set` 객체 | `$set.messages` 배열이 있으면 **목록 전체 교체**. 그 밖(`lastUpdated`, `memoryScratchpad`, `summary`)은 무시 |
/// | `$rewindTo` | 문자열 | 그 id부터 끝까지 잘라냄 |
///
/// 그래서 파일 중간부터 이어 읽을 수 없다. 항상 처음부터 끝까지 읽는다(<see cref="GeminiProvider.AppendOnlySessions"/> = false).
/// </summary>
internal sealed class GeminiTranscriptReader
{
    private const int ToolArgsPreview = 300;

    private readonly List<string> _order = [];
    private readonly Dictionary<string, GeminiMessage> _byId = new(StringComparer.Ordinal);

    private string? _sessionId;
    private string? _projectHash;
    private DateTimeOffset? _startTime;

    /// <summary>줄 하나를 적용한다. 파싱할 수 없는 줄은 건너뛴다.</summary>
    internal void Apply(string line)
    {
        using var document = JsonHelpers.TryParseLine(line);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return;
        }

        if (root.Prop("$rewindTo") is { ValueKind: JsonValueKind.String } rewind)
        {
            RewindTo(rewind.GetString()!);
            return;
        }

        if (root.Prop("$set") is { ValueKind: JsonValueKind.Object } set)
        {
            if (set.Prop("messages") is { ValueKind: JsonValueKind.Array } replaced)
            {
                _order.Clear();
                _byId.Clear();

                foreach (var item in replaced.EnumerateArray())
                {
                    Upsert(item);
                }
            }

            return;
        }

        if (root.Prop("id") is { ValueKind: JsonValueKind.String })
        {
            Upsert(root);
            return;
        }

        if (root.Prop("sessionId") is { ValueKind: JsonValueKind.String })
        {
            _sessionId ??= root.Prop("sessionId").Text();
            _projectHash ??= root.Prop("projectHash").Text();
            _startTime ??= root.Prop("startTime").Timestamp();
        }
    }

    /// <summary>지금까지 적용한 결과.</summary>
    internal GeminiTranscript Result() =>
        new(_sessionId, _projectHash, _startTime, _order.Select(id => _byId[id]).ToList());

    /// <summary>리플레이 결과를 화면·인덱스가 쓰는 메시지 열로 편다. 도구 호출은 어시스턴트 말 뒤에 Tool 한 건씩.</summary>
    internal static IEnumerable<SessionMessage> Flatten(GeminiTranscript transcript)
    {
        foreach (var message in transcript.Messages)
        {
            var role = message.Type switch
            {
                "user" => MessageRole.User,
                "gemini" => MessageRole.Assistant,
                "info" or "warning" or "error" => MessageRole.System,
                _ => (MessageRole?)null,
            };

            if (role is null)
            {
                continue;
            }

            if (message.Text.Length > 0)
            {
                yield return new SessionMessage(message.At, role.Value, message.Text, IsSidechain: false);
            }

            foreach (var call in message.ToolCalls)
            {
                yield return new SessionMessage(message.At, MessageRole.Tool, call, IsSidechain: false);
            }
        }
    }

    private void Upsert(JsonElement element)
    {
        var id = element.Prop("id").Text();
        if (id is null)
        {
            return;
        }

        var parsed = Parse(id, element);

        if (!_byId.ContainsKey(id))
        {
            _order.Add(id);
        }

        _byId[id] = parsed;
    }

    private void RewindTo(string id)
    {
        var index = _order.IndexOf(id);
        if (index < 0)
        {
            return;
        }

        foreach (var removed in _order.Skip(index))
        {
            _byId.Remove(removed);
        }

        _order.RemoveRange(index, _order.Count - index);
    }

    private static GeminiMessage Parse(string id, JsonElement element)
    {
        var type = element.Prop("type").Text() ?? string.Empty;
        var model = element.Prop("model").Text();
        var calls = new List<string>();

        foreach (var call in element.Prop("toolCalls").Items())
        {
            calls.Add(ToolCallText(call));
        }

        return new GeminiMessage(
            id,
            element.Prop("timestamp").Timestamp() ?? default,
            type,
            ContentText(element.Prop("content")),
            model,
            Usage(element.Prop("tokens"), model),
            calls);
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
    /// <c>tokens: {input, output, cached, thoughts, tool, total}</c> — API usageMetadata를 그대로 옮긴 값.
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

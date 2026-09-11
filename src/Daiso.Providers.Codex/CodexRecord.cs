using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Codex;

/// <summary>rollout jsonl 한 줄에서 뽑아낸 것. (ARCHITECTURE §4.2)</summary>
internal sealed record CodexRecord(
    string? SessionId,
    string? Cwd,
    string? CliVersion,
    DateTimeOffset? Timestamp,
    string? Model,
    TokenUsage? CumulativeUsage,
    IReadOnlyList<SessionMessage> Messages);

/// <summary>
/// rollout jsonl 줄을 <see cref="CodexRecord"/>로 바꾼다.
/// 구형(0.147 이하)과 신형(0.153 이상) 형식을 모두 인식한다. <c>cli_version</c>으로 분기하지 않는다.
/// </summary>
internal sealed class CodexRecordParser
{
    private static readonly IReadOnlyList<SessionMessage> NoMessages = [];

    /// <summary>
    /// 같은 응답이 event_msg.agent_message와 response_item 양쪽에 남는 경우가 있다.
    /// 이미 낸 어시스턴트 텍스트면 건너뛴다. (ARCHITECTURE §4.2 중복 방지)
    ///
    /// <para>
    /// <b>바로 앞 하나만 기억하던 때는 놓쳤다.</b> 같은 말이 A B A B 로 번갈아 나오면 둘 다 통과해
    /// 목록·검색에 두 번 보였다 — 실제 인덱스에서 그렇게 생긴 여분 행이 1,953 개였다 (2026-09-11 실측).
    /// 그래서 최근 것을 <see cref="RecentMemory"/> 개까지 기억한다. 다 기억하지 않는 이유는
    /// 세션 하나가 수십 MB 라 본문을 전부 들고 있을 수 없기 때문이고, 중복은 늘 <b>가까이</b> 붙어 나온다.
    /// </para>
    /// </summary>
    private readonly LinkedList<string> _recentAssistant = new();

    private readonly HashSet<string> _recentAssistantIndex = new(StringComparer.Ordinal);

    /// <summary>중복을 알아보려고 기억하는 최근 어시스턴트 텍스트 수.</summary>
    private const int RecentMemory = 64;

    /// <summary>파싱할 수 없거나 무시 대상이면 Messages가 빈 레코드를 돌려준다.</summary>
    internal CodexRecord? Parse(string line)
    {
        using var document = JsonHelpers.TryParseLine(line);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }

        var at = root.Prop("timestamp").Timestamp() ?? default;
        var type = root.Prop("type").Text();
        var payload = root.Prop("payload");

        return type switch
        {
            "session_meta" => Meta(payload, root),
            "event_msg" => EventMessage(payload, at),
            "response_item" => ResponseItem(payload, at),
            "turn_context" => Empty() with
            {
                Cwd = payload.Prop("cwd").Text(),
                Model = payload.Prop("model").Text(),
            },
            "compacted" => Empty() with { Messages = Compacted(payload, at) },
            _ => Empty(),
        };
    }

    private static CodexRecord Empty() => new(null, null, null, null, null, null, NoMessages);

    private static CodexRecord Meta(JsonElement? payload, JsonElement root) => new(
        payload.Prop("id").Text() ?? payload.Prop("session_id").Text(),
        payload.Prop("cwd").Text(),
        payload.Prop("cli_version").Text(),
        payload.Prop("timestamp").Timestamp() ?? root.Prop("timestamp").Timestamp(),
        null,
        null,
        NoMessages);

    private CodexRecord EventMessage(JsonElement? payload, DateTimeOffset at)
    {
        switch (payload.Prop("type").Text())
        {
            // 구형 사용자 메시지
            case "user_message" when payload.Prop("message").Text() is { } message:
                return Empty() with { Messages = [new SessionMessage(at, MessageRole.User, message, false)] };

            // 구형 어시스턴트 메시지
            case "agent_message" when payload.Prop("message").Text() is { } message:
                return Empty() with { Messages = Assistant(at, message) };

            // 신형: 완료된 항목이 종류별로 하나씩 실린다
            case "item_completed":
                return ItemCompleted(payload.Prop("item"), at);

            case "token_count":
                return Empty() with { CumulativeUsage = TotalUsage(payload.Prop("info")) };

            case "thread_settings_applied":
                return Empty() with
                {
                    Model = payload.Path("thread_settings", "model").Text(),
                };

            default:
                return Empty();
        }
    }

    private CodexRecord ItemCompleted(JsonElement? item, DateTimeOffset at)
    {
        switch (item.Prop("type").Text())
        {
            case "UserMessage" when ItemText(item) is { } text:
                return Empty() with { Messages = [new SessionMessage(at, MessageRole.User, text, false)] };

            case "AssistantMessage" or "AgentMessage" when ItemText(item) is { } text:
                return Empty() with { Messages = Assistant(at, text) };

            default:
                return Empty();
        }
    }

    private CodexRecord ResponseItem(JsonElement? payload, DateTimeOffset at)
    {
        switch (payload.Prop("type").Text())
        {
            case "agent_message" when payload.Prop("message").Text() is { } message:
                return Empty() with { Messages = Assistant(at, message) };

            case "message":
                return Message(payload, at);

            // reasoning, function_call*, custom_tool_call* 등은 인덱스에 넣지 않는다
            default:
                return Empty();
        }
    }

    private CodexRecord Message(JsonElement? payload, DateTimeOffset at)
    {
        var text = ItemText(payload);
        if (text is null)
        {
            return Empty();
        }

        return payload.Prop("role").Text() switch
        {
            "assistant" => Empty() with { Messages = Assistant(at, text) },

            // response_item의 user 역할은 사람 입력이 아니라 시스템 주입(AGENTS 지시문, 플러그인 목록 등)이다.
            // 사람이 실제로 입력한 것은 event_msg.user_message / item_completed.UserMessage로 온다.
            "user" or "developer" or "system" => Empty() with
            {
                Messages = [new SessionMessage(at, MessageRole.System, text, false)],
            },
            _ => Empty(),
        };
    }

    private IReadOnlyList<SessionMessage> Assistant(DateTimeOffset at, string text)
    {
        if (!_recentAssistantIndex.Add(text))
        {
            return NoMessages;
        }

        _recentAssistant.AddLast(text);

        if (_recentAssistant.Count > RecentMemory)
        {
            _recentAssistantIndex.Remove(_recentAssistant.First!.Value);
            _recentAssistant.RemoveFirst();
        }

        // 도구 호출·그 결과는 <b>대화가 아니다</b>. Codex 는 그것도 어시스턴트 본문으로 적어 놓는데,
        // 그대로 두면 검색이 파일 읽기 기록으로 덮인다 — 실제 인덱스의 27%(11,424행)가 이것이었다
        // (2026-09-11 실측). ARCHITECTURE §2.2 는 도구 호출을 인덱스에서 빼기로 정해 두었다
        var role = text.StartsWith(ExternalAgentMarker, StringComparison.Ordinal)
            ? MessageRole.Tool
            : MessageRole.Assistant;

        return [new SessionMessage(at, role, text, false)];
    }

    /// <summary>Codex 가 바깥 에이전트의 도구 호출·결과를 적을 때 앞에 붙이는 표시.</summary>
    private const string ExternalAgentMarker = "[external_agent_tool_";

    private static IReadOnlyList<SessionMessage> Compacted(JsonElement? payload, DateTimeOffset at)
    {
        var text = payload.Prop("message").Text()
            ?? payload.Prop("summary").Text()
            ?? ItemText(payload);

        return text is null
            ? NoMessages
            : [new SessionMessage(at, MessageRole.System, text, false)];
    }

    /// <summary>`content[]`의 text 블록을 이어붙인다. 신형은 `type`이 "text" 또는 "Text"다.</summary>
    private static string? ItemText(JsonElement? item)
    {
        var builder = new StringBuilder();

        foreach (var block in item.Prop("content").Items())
        {
            if (block.Prop("text").Text() is { } text)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(text);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>`info.total_token_usage`는 세션 누적값이다. info가 null인 레코드가 있다.</summary>
    private static TokenUsage? TotalUsage(JsonElement? info)
    {
        var total = info.Prop("total_token_usage");
        if (total is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        return new TokenUsage(
            total.Prop("input_tokens").NumberOrZero(),
            total.Prop("output_tokens").NumberOrZero(),
            total.Prop("cache_write_input_tokens").NumberOrZero(),
            total.Prop("cached_input_tokens").NumberOrZero(),
            null);
    }
}

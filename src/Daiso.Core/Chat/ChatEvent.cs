namespace Daiso.Core.Chat;

/// <summary>
/// 챗봇 엔진이 CLI의 스트리밍 JSON을 읽어 올려 보내는 사건 하나. 화면(방)이 이걸로 말풍선을 채운다. (FEATURE_PLAN B 모드)
/// 터미널을 띄우지 않고, CLI를 stdin/stdout JSON으로 직접 다뤄 진짜 챗봇처럼 만든다.
/// </summary>
public abstract record ChatEvent
{
    /// <summary>세션이 시작됐다. 모델·세션 id 등 메타.</summary>
    public sealed record Started(string? SessionId, string? Model) : ChatEvent;

    /// <summary>어시스턴트 답의 한 조각(글자 단위 스트리밍). 지금 말풍선 뒤에 이어 붙인다.</summary>
    public sealed record AssistantDelta(string Text) : ChatEvent;

    /// <summary>어시스턴트 답 한 덩어리가 끝났다(스트리밍을 못 받은 경우의 대체).</summary>
    public sealed record AssistantMessage(string Text) : ChatEvent;

    /// <summary>도구 호출. 카드로 접어 보여 준다. <paramref name="Summary"/>는 이름과 인자 요약(비밀은 담지 않는다).</summary>
    public sealed record ToolUse(string Id, string Name, string Summary) : ChatEvent;

    /// <summary>도구 결과. 카드 아래에 붙인다.</summary>
    public sealed record ToolResult(string Id, string Summary, bool IsError) : ChatEvent;

    /// <summary>
    /// 도구를 써도 되는지 묻는다. 대화 안에서 허용/거부 카드로 그린다. <see cref="RequestId"/>로 답한다.
    /// </summary>
    public sealed record PermissionRequest(string RequestId, string ToolName, string Summary) : ChatEvent;

    /// <summary>한 턴이 끝났다. 입력을 다시 받을 수 있다.</summary>
    public sealed record TurnEnded(bool IsError, string? Message) : ChatEvent;

    /// <summary>모르는 줄. 무시하지만 진단용으로 남긴다.</summary>
    public sealed record Ignored(string Reason) : ChatEvent;
}

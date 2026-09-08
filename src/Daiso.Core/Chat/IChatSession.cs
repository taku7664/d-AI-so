namespace Daiso.Core.Chat;

/// <summary>
/// 터미널 없이 CLI를 챗봇처럼 다루는 세션. 사용자 메시지를 보내고, 답·도구·승인 요청을 사건으로 받는다. (FEATURE_PLAN B 모드)
/// 구현은 도구마다 다르다(Claude는 stream-json). 화면(방)은 이 인터페이스에만 기댄다.
/// </summary>
public interface IChatSession : IDisposable
{
    /// <summary>엔진이 올리는 사건. 백그라운드 스레드에서 온다. 화면은 자기 디스패처로 옮겨 써야 한다.</summary>
    event Action<ChatEvent> Event;

    /// <summary>프로세스가 끝났다. 인자는 종료 코드.</summary>
    event Action<int> Exited;

    /// <summary>끝났는가.</summary>
    bool HasExited { get; }

    /// <summary>사용자 메시지를 보낸다.</summary>
    void Send(string text);

    /// <summary>도구 승인 요청(<see cref="ChatEvent.PermissionRequest"/>)에 답한다.</summary>
    void Respond(string requestId, bool allow);
}

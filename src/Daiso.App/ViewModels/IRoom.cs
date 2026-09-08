using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 방 하나의 공통 얼굴. 두 종류가 있다: 터미널 방(<see cref="ChatRoomViewModel"/>, xterm) 과 챗봇 방(<see cref="StreamingRoomViewModel"/>, 말풍선).
/// 탭 띠·<see cref="RoomManager"/>·앱 종료 정리는 이 인터페이스에만 기댄다. 실제 화면은 방 종류에 맞는 것을 쓴다. (FEATURE_PLAN)
/// </summary>
public interface IRoom : IDisposable
{
    /// <summary>제목(도구 · 폴더).</summary>
    string Title { get; }

    /// <summary>탭에 다는 짧은 이름(폴더).</summary>
    string ShortTitle { get; }

    /// <summary>도구 아바타 글자.</summary>
    string Avatar { get; }

    /// <summary>도구 아바타 색.</summary>
    Microsoft.UI.Xaml.Media.Brush AvatarBrush { get; }

    /// <summary>안 본 새 답이 있으면 탭에 점.</summary>
    Visibility UnseenVisibility { get; }

    /// <summary>프로세스가 살아 있는가.</summary>
    bool IsRunning { get; }

    /// <summary>이 탭을 보기 시작했다(안 본 표시 지움).</summary>
    void MarkActive();

    /// <summary>다른 탭으로 옮겨 갔다.</summary>
    void MarkInactive();
}

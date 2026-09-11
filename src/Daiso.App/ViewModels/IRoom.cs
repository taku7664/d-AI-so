using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 방 하나의 공통 얼굴. 두 종류가 있다: 터미널 방(<see cref="TerminalRoomViewModel"/>, xterm) 과 챗봇 방(<see cref="StreamingRoomViewModel"/>, 말풍선).
/// 탭 띠·<see cref="RoomManager"/>·앱 종료 정리는 이 인터페이스에만 기댄다. 실제 화면은 방 종류에 맞는 것을 쓴다. (FEATURE_PLAN)
/// 변경 알림을 구현해 탭이 제목·안 본 점을 따라간다.
/// </summary>
public interface IRoom : IDisposable, INotifyPropertyChanged
{
    /// <summary>이 방을 가리키는 값. 윈도우 알림이 "어느 방이 끝났는지" 를 이걸로 들고 다닌다.</summary>
    string Id { get; }

    /// <summary>제목(도구 · 폴더, 프로세스가 제목을 보내면 그 뒤에). 탭 툴팁.</summary>
    string Title { get; }

    /// <summary>탭에 다는 짧은 이름(폴더).</summary>
    string ShortTitle { get; }

    /// <summary>어느 도구의 방인가. 탭 아이콘(로고)에 쓴다.</summary>
    Daiso.Core.ToolKind Tool { get; }

    /// <summary>안 본 새 답이 있으면 탭에 점.</summary>
    Visibility UnseenVisibility { get; }

    /// <summary>프로세스가 살아 있는가.</summary>
    bool IsRunning { get; }

    /// <summary>이 탭을 보기 시작했다(안 본 표시 지움).</summary>
    void MarkActive();

    /// <summary>다른 탭으로 옮겨 갔다.</summary>
    void MarkInactive();
}

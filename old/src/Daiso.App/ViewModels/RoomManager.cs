using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 열려 있는 대화 방을 들고 있는 하나뿐인 자리. 화면을 옮겨도 방은 여기 살아 있어,
/// 터미널 화면으로 돌아오면 그대로 다시 보인다. 앱을 닫을 때 전부 정리한다. (ARCHITECTURE §5.3)
/// 방들의 "안 본 답"을 합쳐 좌측 메뉴의 터미널 항목에 빨간 점을 켠다.
/// </summary>
public sealed partial class RoomManager : ObservableObject
{
    /// <summary>열린 방. 탭 띠가 이걸 그린다.</summary>
    public ObservableCollection<IRoom> Rooms { get; } = [];

    /// <summary>
    /// 지금 보고 있는 방. null 이면 새 터미널 카드.
    /// 터미널 화면이 여기에 적고, 뒤로/앞으로가 여기에 써서 방을 되돌린다.
    /// </summary>
    [ObservableProperty]
    private IRoom? activeRoom;

    /// <summary>창이 앞에 있는가. 셸이 <c>Activated</c> 에서 적는다.</summary>
    private bool _windowActive = true;

    /// <summary>터미널 화면이 열려 있는가. 셸이 화면을 옮길 때 적는다.</summary>
    private bool _terminalVisible;

    /// <summary>창이 앞으로 나오거나 뒤로 갔다.</summary>
    public void SetWindowActive(bool active)
    {
        _windowActive = active;
        ApplyWatching();
    }

    /// <summary>터미널 화면을 열었거나 떠났다.</summary>
    public void SetTerminalVisible(bool visible)
    {
        _terminalVisible = visible;
        ApplyWatching();
    }

    partial void OnActiveRoomChanged(IRoom? value) => ApplyWatching();

    /// <summary>
    /// <b>정말 보고 있는 방만</b> 본 것으로 친다: 창이 앞에 있고, 터미널 화면이 열려 있고, 그 방 탭이 골라져 있을 때.
    ///
    /// <para>
    /// 예전에는 터미널 화면이 방을 바꿀 때만 이 표시를 옮겼다. 그래서 요약 화면으로 가거나 다른 앱을 쓰는 동안에는
    /// 마지막에 보던 방이 계속 "보고 있음" 으로 남아, 도구가 답을 마쳐도 점이 켜지지 않았다 —
    /// 자리를 비운 사이에 끝난 일을 알 방법이 없었다 (2026-09-11 사람의 지적).
    /// </para>
    /// </summary>
    private void ApplyWatching()
    {
        var watching = _windowActive && _terminalVisible;

        foreach (var room in Rooms)
        {
            if (watching && ReferenceEquals(room, ActiveRoom))
            {
                room.MarkActive();
            }
            else
            {
                room.MarkInactive();
            }
        }
    }

    /// <summary>어느 방이든 안 본 답이 있는가.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnseenVisibility))]
    private bool hasUnseen;

    public Visibility UnseenVisibility => HasUnseen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>새 방을 더한다.</summary>
    public void Add(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        room.PropertyChanged += OnRoomChanged;
        Rooms.Add(room);
        ApplyWatching();
        Recompute();
    }

    /// <summary>방을 닫고 프로세스를 정리한다.</summary>
    public void Close(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        room.PropertyChanged -= OnRoomChanged;
        Rooms.Remove(room);
        _announced.Remove(room.Id);
        room.Dispose();
        Recompute();
    }

    /// <summary>살아 있는 방이 있는가. 앱을 닫을 때 물어볼지 판단한다.</summary>
    public bool HasRooms => Rooms.Count > 0;

    /// <summary>
    /// 모든 방을 정리한다(자식 프로세스 트리 kill). 앱 종료 때.
    /// <para>
    /// <b>한꺼번에 정리한다.</b> 방 하나를 닫는 일은 읽기 루프가 빠져나오기를 최대 2초 기다리는데,
    /// 차례로 하면 방 넷이면 8초 동안 창이 닫히지 않고 멈춰 있었다 (2026-09-11 점검).
    /// 방끼리는 서로 무관하므로 같이 기다리면 전체가 그 2초 안에 끝난다.
    /// </para>
    /// </summary>
    public void DisposeAll()
    {
        var rooms = Rooms.ToList();

        foreach (var room in rooms)
        {
            room.PropertyChanged -= OnRoomChanged;
        }

        Rooms.Clear();
        Recompute();

        if (rooms.Count > 0)
        {
            Parallel.ForEach(rooms, room => room.Dispose());
        }
    }

    /// <summary>
    /// 보고 있지 않은 방의 일이 끝났다. 셸이 이걸 받아 윈도우 알림을 띄운다.
    /// <b>한 번 끝날 때 한 번만</b> 나온다 — 점이 꺼졌다가 다시 켜져야 다음 번이다.
    /// </summary>
    public event EventHandler<IRoom>? RoomFinished;

    /// <summary>이미 알린 방. 점이 꺼지면 여기서 빠진다.</summary>
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    private void OnRoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IRoom.UnseenVisibility) or "HasUnseen")
        {
            if (sender is IRoom room)
            {
                Announce(room);
            }

            Recompute();
        }
    }

    /// <summary>점이 새로 켜졌으면 한 번 알린다. 꺼졌으면 다음 번을 위해 잊는다.</summary>
    private void Announce(IRoom room)
    {
        if (room.UnseenVisibility == Visibility.Visible)
        {
            if (_announced.Add(room.Id))
            {
                RoomFinished?.Invoke(this, room);
            }

            return;
        }

        _announced.Remove(room.Id);
    }

    private void Recompute() => HasUnseen = Rooms.Any(room => room.UnseenVisibility == Visibility.Visible);
}

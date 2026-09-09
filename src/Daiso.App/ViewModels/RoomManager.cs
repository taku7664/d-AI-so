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
        Recompute();
    }

    /// <summary>방을 닫고 프로세스를 정리한다.</summary>
    public void Close(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        room.PropertyChanged -= OnRoomChanged;
        Rooms.Remove(room);
        room.Dispose();
        Recompute();
    }

    /// <summary>살아 있는 방이 있는가. 앱을 닫을 때 물어볼지 판단한다.</summary>
    public bool HasRooms => Rooms.Count > 0;

    /// <summary>모든 방을 정리한다(자식 프로세스 트리 kill). 앱 종료 때.</summary>
    public void DisposeAll()
    {
        foreach (var room in Rooms)
        {
            room.PropertyChanged -= OnRoomChanged;
            room.Dispose();
        }

        Rooms.Clear();
        Recompute();
    }

    private void OnRoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IRoom.UnseenVisibility) or "HasUnseen")
        {
            Recompute();
        }
    }

    private void Recompute() => HasUnseen = Rooms.Any(room => room.UnseenVisibility == Visibility.Visible);
}

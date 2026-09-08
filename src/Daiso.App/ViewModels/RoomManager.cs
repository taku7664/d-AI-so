using System.Collections.ObjectModel;

namespace Daiso.App.ViewModels;

/// <summary>
/// 열려 있는 대화 방을 들고 있는 하나뿐인 자리. 화면을 옮겨도 방은 여기 살아 있어,
/// 터미널 화면으로 돌아오면 그대로 다시 보인다. 앱을 닫을 때 전부 정리한다. (ARCHITECTURE §5.3)
/// </summary>
public sealed class RoomManager
{
    /// <summary>열린 방. 탭 띠가 이걸 그린다.</summary>
    public ObservableCollection<IRoom> Rooms { get; } = [];

    /// <summary>새 방을 더한다.</summary>
    public void Add(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        Rooms.Add(room);
    }

    /// <summary>방을 닫고 프로세스를 정리한다.</summary>
    public void Close(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        Rooms.Remove(room);
        room.Dispose();
    }

    /// <summary>살아 있는 방이 있는가. 앱을 닫을 때 물어볼지 판단한다.</summary>
    public bool HasRooms => Rooms.Count > 0;

    /// <summary>모든 방을 정리한다(자식 프로세스 트리 kill). 앱 종료 때.</summary>
    public void DisposeAll()
    {
        foreach (var room in Rooms)
        {
            room.Dispose();
        }

        Rooms.Clear();
    }
}

using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Daiso.App.Services;

/// <summary>
/// 도구가 일을 마쳤을 때 <b>윈도우 알림</b>(오른쪽 아래)을 띄운다. 누르면 그 방으로 데려간다. (2026-09-11 사람의 요청)
///
/// <para>
/// 언제 띄우는가는 <see cref="RoomManager"/> 가 정한다 — 보고 있지 않은 방에 답이 와서
/// 탭에 초록 점이 켜지는 바로 그 순간이다. 보고 있는 방은 알릴 것이 없다.
/// </para>
///
/// <para>
/// <b>한 번 끝날 때 한 번만</b> 띄운다. 도구는 답을 여러 조각으로 내놓는데 조각마다 알리면 알림이 쌓인다.
/// 점이 꺼졌다가(사람이 보러 갔다) 다시 켜질 때 비로소 다음 알림이다.
/// </para>
///
/// <para>
/// 이 앱은 설치 없이 도는(unpackaged) 앱이라 <see cref="AppNotificationManager.Register()"/> 가
/// 알림 클릭을 받아 줄 자리를 등록한다. 앱이 떠 있으면 그 프로세스가 그대로 클릭을 받는다.
/// 알림을 쓸 수 없는 환경(정책으로 꺼짐 등)에서는 조용히 아무 일도 하지 않는다 — 점은 그대로 뜬다.
/// </para>
/// </summary>
public sealed class DoneNotifier : IDisposable
{
    /// <summary>알림에 실어 보내는 방 값의 이름.</summary>
    private const string RoomArgument = "room";

    private readonly DispatcherQueue _dispatcher;
    private readonly Action<string> _goToRoom;
    private bool _registered;

    /// <param name="dispatcher">화면 스레드. 클릭은 다른 스레드로 들어온다.</param>
    /// <param name="goToRoom">이 방으로 데려가는 일. 셸이 건넨다.</param>
    public DoneNotifier(DispatcherQueue dispatcher, Action<string> goToRoom)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(goToRoom);

        _dispatcher = dispatcher;
        _goToRoom = goToRoom;
    }

    /// <summary>
    /// 알림을 받을 자리를 등록한다. 실패해도 앱은 그대로 돈다.
    /// <para>
    /// <b>이름과 아이콘을 함께 준다.</b> 그냥 등록하면 알림에 실행 파일 이름이 뜨고,
    /// 앱이 꺼져 있을 때 누른 알림을 받아 줄 자리도 남지 않는다.
    /// </para>
    /// </summary>
    public void Start()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;

            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "daiso.ico");

            if (File.Exists(icon))
            {
                AppNotificationManager.Default.Register(UiStrings.Get("App_Name"), new Uri(icon));
            }
            else
            {
                AppNotificationManager.Default.Register();
            }

            _registered = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException or UriFormatException or System.Runtime.InteropServices.COMException)
        {
            // 이름·아이콘까지는 안 되더라도 알림 자체는 살려 본다
            _registered = TryPlainRegister();
        }
    }

    /// <summary>이름 없이 등록한다. 이것마저 안 되면 알림을 접는다 — 탭의 초록 점만으로 알린다.</summary>
    private static bool TryPlainRegister()
    {
        try
        {
            AppNotificationManager.Default.Register();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>일이 끝났다고 알린다.</summary>
    /// <param name="room">끝난 방.</param>
    public void Notify(IRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        if (!_registered)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument(RoomArgument, room.Id)
                .AddText(UiStrings.Get("Room_DoneTitle"))
                .AddText(room.Title)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // 알림 하나를 놓친 것뿐이다. 점은 그대로 켜져 있다
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue(RoomArgument, out var id) || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        _dispatcher.TryEnqueue(() => _goToRoom(id));
    }

    /// <summary>
    /// 듣기를 그만둔다.
    ///
    /// <para>
    /// <b><see cref="AppNotificationManager.Unregister"/> 는 부르지 않는다.</b> 창이 닫히는 길에서 이것이 던졌고,
    /// 그 예외가 충돌 보고기로 돌아가 끝없이 되돌았다 — 창 없는 프로세스가 죽지 않고 남았다 (2026-09-11 재현).
    /// 등록은 이 PC 에 남아 있어야 하는 것이기도 하다: 앱이 꺼진 뒤에 누른 알림도 앱을 찾아올 수 있어야 한다.
    /// 지우는 것은 앱을 지울 때 할 일이지 창을 닫을 때 할 일이 아니다.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // 종료 중이다. 더 할 일이 없다
        }

        _registered = false;
    }
}

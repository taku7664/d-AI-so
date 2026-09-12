using System.Runtime.InteropServices;

namespace Daiso.App.Services;

/// <summary>
/// 작업 표시줄 오른쪽 끝(알림 영역)의 아이콘. (2026-09-12 사람의 요청)
///
/// <para>
/// 창을 닫아도 앱은 남는다. 그러면 <b>돌아올 문</b>과 <b>정말 끝내는 문</b>이 있어야 한다 —
/// 둘 다 없으면 사람이 작업 관리자로 가야 한다. 여기가 그 두 문이다.
/// </para>
///
/// <para>
/// WinUI 는 이 자리를 다루는 길을 주지 않아 Win32 를 직접 쓴다. 아이콘은 창에 달리는 것이 아니라
/// <b>메시지만 받는 창</b>(HWND_MESSAGE)에 달린다 — 그래야 앱 창을 숨겨도 아이콘이 살아 있다.
/// </para>
///
/// <para>
/// <b>아이콘에 고정된 값을 붙인다(<c>NIF_GUID</c>).</b> 윈도우는 앱이 <b>강제로</b> 죽으면 아이콘을 바로 치우지 않는다 —
/// 그 자리를 건드릴 때에야 주인이 없는 것을 알아채고 지운다(윈도우 전체의 동작이지 이 앱만의 일이 아니다).
/// 그동안 앱을 다시 켜면 껍데기 옆에 새 아이콘이 붙어 <b>여러 개로 보였다</b> (2026-09-12 사람의 지적).
/// 같은 값으로 등록하면 윈도우가 그 자리를 <b>갈아 끼운다</b> — 늘지 않는다.
/// </para>
///
/// <para>
/// 다만 이 값은 실행 파일 위치와 함께 기억된다. 파일을 옮기면 등록이 거절될 수 있어,
/// 그때는 값 없이 한 번 더 시도한다 — 아이콘이 하나 더 보이는 것이 아예 없는 것보다 낫다.
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int TrayCallback = WM_APP + 1;

    private const int WM_COMMAND = 0x0111;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_DESTROY = 0x0002;

    private const int MenuShow = 1;
    private const int MenuExit = 2;

    private readonly WndProc _wndProc;
    private readonly string _showText;
    private readonly string _exitText;
    private IntPtr _window;
    private IntPtr _icon;
    private bool _added;

    /// <summary>등록한 창 클래스 이름. 끝낼 때 이 이름으로 되돌린다.</summary>
    private string? _className;

    /// <summary>클래스 등록이 실제로 됐는가. 안 됐으면 되돌릴 것도 없다.</summary>
    private bool _classRegistered;

    /// <summary>
    /// 아이콘이 정말 붙었는가. <b>붙지 않았으면 창을 숨겨서는 안 된다</b> —
    /// 돌아올 문도 끝내는 문도 없이 앱이 갇힌다.
    /// </summary>
    public bool IsAvailable => _added;

    /// <summary>아이콘을 눌렀다. 창을 다시 보여 달라는 뜻이다.</summary>
    public event Action? ShowRequested;

    /// <summary>메뉴에서 끝내기를 골랐다. 이때만 정말 끝낸다.</summary>
    public event Action? ExitRequested;

    /// <param name="tooltip">아이콘에 마우스를 올렸을 때 뜨는 글.</param>
    /// <param name="iconPath">`.ico` 파일. 없으면 아이콘 없이 지나간다.</param>
    /// <param name="showText">메뉴의 "열기".</param>
    /// <param name="exitText">메뉴의 "끝내기".</param>
    public TrayIcon(string tooltip, string iconPath, string showText, string exitText)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        ArgumentNullException.ThrowIfNull(iconPath);

        _showText = showText;
        _exitText = exitText;
        _wndProc = HandleMessage;

        _className = "DaisoTray_" + Guid.NewGuid().ToString("N");
        var className = _className;
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };

        if (RegisterClassEx(ref wndClass) == 0)
        {
            return;
        }

        _classRegistered = true;

        // HWND_MESSAGE(-3) 아래에 두면 화면에 뜨지 않고 메시지만 받는다
        _window = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            return;
        }

        if (File.Exists(iconPath))
        {
            _icon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadFromFile | DefaultSize);
        }

        var data = NewData();
        data.uFlags = NifMessage | NifIcon | NifTip | NifGuid;
        data.uCallbackMessage = TrayCallback;
        data.hIcon = _icon;
        data.szTip = tooltip;
        data.guidItem = IconId;

        _added = Shell_NotifyIcon(NimAdd, ref data);

        if (!_added)
        {
            // 값이 다른 위치에 묶여 있어 거절됐다. 값 없이 붙인다
            _useGuid = false;
            data = NewData();
            data.uFlags = NifMessage | NifIcon | NifTip;
            data.uCallbackMessage = TrayCallback;
            data.hIcon = _icon;
            data.szTip = tooltip;

            _added = Shell_NotifyIcon(NimAdd, ref data);
        }
    }

    /// <summary>아이콘에서 잠깐 뜨는 말풍선. 창이 어디로 갔는지 한 번 알려 주는 데 쓴다.</summary>
    public void ShowBalloon(string title, string body)
    {
        if (!_added)
        {
            return;
        }

        var data = NewData();
        data.uFlags |= NifInfo;
        data.szInfoTitle = title;
        data.szInfo = body;
        data.dwInfoFlags = 0;

        Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>이 앱의 아이콘을 가리키는 고정된 값. 다시 켜도 같은 자리를 쓴다.</summary>
    private static readonly Guid IconId = new("3F2A6C51-9B4E-4D27-8C10-DA150A1C7E63");

    /// <summary>고정된 값으로 붙였는가. 거절돼 값 없이 붙였으면 지울 때도 값을 쓰면 안 된다.</summary>
    private bool _useGuid = true;

    private NOTIFYICONDATA NewData()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        if (_useGuid)
        {
            data.uFlags = NifGuid;
            data.guidItem = IconId;
        }

        return data;
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case TrayCallback:
                switch ((int)lParam)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        ShowRequested?.Invoke();
                        break;
                    case WM_RBUTTONUP:
                        ShowMenu();
                        break;
                    default:
                        break;
                }

                return IntPtr.Zero;

            case WM_COMMAND:
                switch ((int)wParam & 0xFFFF)
                {
                    case MenuShow:
                        ShowRequested?.Invoke();
                        break;
                    case MenuExit:
                        ExitRequested?.Invoke();
                        break;
                    default:
                        break;
                }

                return IntPtr.Zero;

            case WM_DESTROY:
                return IntPtr.Zero;

            default:
                return DefWindowProc(hWnd, message, wParam, lParam);
        }
    }

    /// <summary>오른쪽 단추 메뉴. 열기·끝내기 둘뿐이다.</summary>
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();

        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, 0, MenuShow, _showText);
            AppendMenu(menu, 0x800, 0, string.Empty); // MF_SEPARATOR
            AppendMenu(menu, 0, MenuExit, _exitText);

            GetCursorPos(out var point);

            // 메뉴를 띄우기 전에 이 창을 앞으로 세우지 않으면, 다른 곳을 눌러도 메뉴가 안 닫힌다(오래된 Win32 규칙)
            SetForegroundWindow(_window);
            TrackPopupMenuEx(menu, 0x0080 /* TPM_RIGHTBUTTON */, point.X, point.Y, _window, IntPtr.Zero);
            PostMessage(_window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        // 창을 없앤 다음에 클래스를 되돌린다. 창이 남아 있으면 UnregisterClass 가 거절한다
        if (_classRegistered && _className is { } name)
        {
            UnregisterClass(name, GetModuleHandle(null));
            _classRegistered = false;
        }
    }

    // ── Win32 ────────────────────────────────────────────────────────────

    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;

    private const int NifMessage = 0x01;
    private const int NifIcon = 0x02;
    private const int NifTip = 0x04;
    private const int NifInfo = 0x10;
    private const int NifGuid = 0x20;

    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint DefaultSize = 0x0040;

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int id, string item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}

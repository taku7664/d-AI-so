using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Daiso.App.Services;

/// <summary>탐색기에서 끌어온 파일을 받을 페이지가 구현한다. 셸이 지금 페이지에 물어서 넘긴다.</summary>
public interface IFileDropSink
{
    /// <summary>파일 드래그가 창에 들어왔다. 받는 판을 보이는 데 쓴다.</summary>
    void FileDragStarted();

    /// <summary>드래그가 창을 떠났거나 놓였다. 받는 판을 걷는다.</summary>
    void FileDragEnded();

    /// <summary>파일이 놓였다. 경로는 전체 경로다.</summary>
    Task FilesDroppedAsync(IReadOnlyList<string> paths);
}

/// <summary>
/// 창의 OLE 드롭 대상. 파일(CF_HDROP)만 받는다.
///
/// 왜 XAML 의 AllowDrop/Drop 을 쓰지 않는가: 이 앱(Windows 10 · unpackaged · WinAppSDK 1.8)에서는 XAML 이 어떤 HWND 에도
/// 드롭 대상을 등록하지 않는다(2026-09-09 측정: 모든 창의 OleDropTargetInterface 속성 없음 → 금지 커서, DragEnter 도 오지 않음).
/// 그래서 표준 OLE 대로 우리가 직접 RegisterDragDrop 한다. 커서 아래 HWND 가 대상이 되므로 자식 창까지 모두 등록한다.
/// 놓인 파일은 셸이 지금 페이지(<see cref="IFileDropSink"/>)에 넘긴다.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FileDropTarget : FileDropTarget.IDropTarget
{
    private readonly Func<IFileDropSink?> _sink;
    private bool _accepting;

    public FileDropTarget(Func<IFileDropSink?> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// 최상위 창과 그 자식 창 전부에 이 대상을 등록한다. 이미 등록된 창(DRAGDROP_E_ALREADYREGISTERED)은 넘어간다.
    /// 자식 창(XAML 아일랜드·WebView2)은 나중에 생기므로 페이지를 옮길 때마다 다시 부른다.
    /// </summary>
    public void RegisterAll(IntPtr topLevel)
    {
        Register(topLevel);
        EnumChildWindows(topLevel, (child, _) => { Register(child); return true; }, IntPtr.Zero);
    }

    private void Register(IntPtr hwnd)
    {
        if (GetProp(hwnd, "OleDropTargetInterface") != IntPtr.Zero)
        {
            return;
        }

        _ = RegisterDragDrop(hwnd, this);
    }

    // ── IDropTarget ─────────────────────────────────────────────────────────

    int IDropTarget.DragEnter(IDataObject data, uint keyState, POINTL point, ref uint effect)
    {
        _accepting = HasFiles(data) && _sink() is not null;
        effect = _accepting ? DROPEFFECT_COPY : DROPEFFECT_NONE;

        if (_accepting)
        {
            _sink()?.FileDragStarted();
        }

        return S_OK;
    }

    int IDropTarget.DragOver(uint keyState, POINTL point, ref uint effect)
    {
        effect = _accepting ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        return S_OK;
    }

    int IDropTarget.DragLeave()
    {
        if (_accepting)
        {
            _sink()?.FileDragEnded();
        }

        _accepting = false;
        return S_OK;
    }

    int IDropTarget.Drop(IDataObject data, uint keyState, POINTL point, ref uint effect)
    {
        var sink = _sink();
        var paths = _accepting && sink is not null ? ReadPaths(data) : [];
        effect = paths.Count > 0 ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        _accepting = false;

        sink?.FileDragEnded();
        if (paths.Count > 0 && sink is not null)
        {
            // 드롭 콜백 안에서 오래 기다리면 탐색기가 멈춘다. 붙이는 일은 바로 돌려주고 뒤에서 한다
            _ = sink.FilesDroppedAsync(paths);
        }

        return S_OK;
    }

    // ── CF_HDROP ─────────────────────────────────────────────────────────────

    private static FORMATETC HDropFormat() => new()
    {
        cfFormat = CF_HDROP,
        ptd = IntPtr.Zero,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = TYMED.TYMED_HGLOBAL,
    };

    private static bool HasFiles(IDataObject data)
    {
        var format = HDropFormat();
        return data.QueryGetData(ref format) == S_OK;
    }

    private static IReadOnlyList<string> ReadPaths(IDataObject data)
    {
        var format = HDropFormat();
        data.GetData(ref format, out var medium);

        try
        {
            if (medium.unionmember == IntPtr.Zero)
            {
                return [];
            }

            var count = DragQueryFile(medium.unionmember, 0xFFFFFFFF, null, 0);
            var paths = new List<string>((int)count);
            var buffer = new StringBuilder(1024);

            for (uint i = 0; i < count; i++)
            {
                buffer.Clear();
                var length = DragQueryFile(medium.unionmember, i, buffer, (uint)buffer.Capacity);
                if (length > 0)
                {
                    paths.Add(buffer.ToString(0, (int)length));
                }
            }

            return paths;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    // ── 네이티브 ─────────────────────────────────────────────────────────────

    private const int S_OK = 0;
    private const short CF_HDROP = 15;
    private const uint DROPEFFECT_NONE = 0;
    private const uint DROPEFFECT_COPY = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] IDataObject data, [In] uint keyState, [In] POINTL point, [In, Out] ref uint effect);

        [PreserveSig]
        int DragOver([In] uint keyState, [In] POINTL point, [In, Out] ref uint effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] IDataObject data, [In] uint keyState, [In] POINTL point, [In, Out] ref uint effect);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] IDropTarget target);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint index, StringBuilder? buffer, uint capacity);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr hwnd, string name);
}

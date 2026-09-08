using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Daiso.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Daiso.App.Terminal;

/// <summary>
/// 앱 안의 터미널 화면. WebView2에 동봉한 xterm.js(Assets/xterm)를 로컬 가상 호스트로 띄우고
/// <see cref="PtySession"/>과 메시지로 잇는다. 네트워크는 쓰지 않는다. (ARCHITECTURE §5.3)
///
/// 앱 → 페이지: out(base64 UTF-8) · paste(text) · theme · focus · fit · clear
/// 페이지 → 앱: in(키 입력) · resize(cols,rows) · copy(text) · paste(요청) · ready · title
/// </summary>
public sealed class TerminalHost : UserControl
{
    private const string VirtualHost = "daiso.terminal";

    private readonly WebView2 _web = new();
    private readonly List<string> _pendingOut = [];
    private ChatRoomViewModel? _room;
    private Action<byte[]>? _roomSink;
    private int _generation;
    private bool _ready;
    private bool _initialized;

    public TerminalHost()
    {
        Content = _web;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ActualThemeChanged += (_, _) => PostTheme();
    }

    /// <summary>xterm이 뜨고 첫 크기를 보고했다. 그 뒤부터 출력이 바로 그려진다.</summary>
    public event EventHandler? Ready;

    /// <summary>프로세스가 제목을 바꿨다(OSC 0). 방 탭 이름에 쓴다.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>동봉한 xterm 파일 폴더.</summary>
    public static string AssetFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "xterm");

    /// <summary>WebView2 런타임(Edge Evergreen)이 있는가. 없으면 내장 터미널을 켤 수 없어 외부 터미널로 간다.</summary>
    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>WebView2를 만들고 xterm 페이지를 연다. 한 번만 하면 된다.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        // unpackaged 앱의 기본 사용자 데이터 폴더는 exe 옆이라 쓰기가 막힐 수 있다. 앱 설정과 같은 곳(%LOCALAPPDATA%\d-AI-so) 아래에 둔다
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d-AI-so", "WebView2");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", dataFolder);

        await _web.EnsureCoreWebView2Async();

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        core.SetVirtualHostNameToFolderMapping(VirtualHost, AssetFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;

        _web.Source = new Uri($"https://{VirtualHost}/index.html");
    }

    /// <summary>
    /// 방을 화면에 잇는다. xterm을 비우고 그 방의 지난 화면을 되돌린 뒤 이후 출력을 잇는다.
    /// 다른 방으로 바꾸면 이전 방 구독을 끊고 새 방을 되돌린다. 하나의 호스트를 탭마다 다시 가리켜 쓴다.
    /// </summary>
    public void BindRoom(ChatRoomViewModel room)
    {
        ArgumentNullException.ThrowIfNull(room);

        if (ReferenceEquals(_room, room))
        {
            return;
        }

        // 이전 방에서 떼어 낸다. 세대 번호를 올려, 떼어 내는 순간 이미 큐에 오른 이전 방의 늦은 덩어리는 버린다.
        // 호스트가 하나뿐이라 이 방어가 없으면 옛 방의 바이트가 새 방 화면에 섞여 들어온다.
        if (_room is not null && _roomSink is not null)
        {
            _room.DetachHost(_roomSink);
        }

        _room = room;
        var generation = ++_generation;
        _roomSink = bytes =>
        {
            if (generation == _generation)
            {
                PostOutput(Convert.ToBase64String(bytes));
            }
        };

        // 화면을 비우고 이 방의 버퍼를 통째로 되돌린 뒤 이후 출력을 받는다(원자적 붙이기)
        Post(new { type = "clear" });
        _pendingOut.Clear();
        room.AttachHost(_roomSink);
    }

    /// <summary>터미널 화면에서 글자를 찾는다. 다음/이전으로 이동.</summary>
    public void Find(string query, bool previous) =>
        Post(new { type = "find", query, dir = previous ? "prev" : "next" });

    /// <summary>찾기 표시를 지운다.</summary>
    public void ClearFind() => Post(new { type = "find-clear" });

    /// <summary>키보드 포커스를 터미널로.</summary>
    public void FocusTerminal()
    {
        _web.Focus(FocusState.Programmatic);
        Post(new { type = "focus" });
    }

    private void PostOutput(string base64)
    {
        if (!_ready)
        {
            _pendingOut.Add(base64);
            return;
        }

        Post(new { type = "out", data = base64 });
    }

    private void Post(object message)
    {
        if (_web.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        }
    }

    private void PostTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark
            || (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var fontSize = Math.Clamp(App.Services.GetRequiredService<Services.ISettingsStore>().Current.TerminalFontSize, 8, 28);

        // xterm ITheme. 카드 배경과 비슷한 색을 써 화면 안에서 튀지 않게 한다
        var theme = dark
            ? new
            {
                fontSize,
                background = "#1f1f1f",
                foreground = "#d6d6d6",
                cursor = "#ffffff",
                selectionBackground = "#3a5a8c80",
                black = "#1f1f1f", red = "#f47067", green = "#57ab5a", yellow = "#c69026", blue = "#539bf5", magenta = "#b083f0", cyan = "#39c5cf", white = "#adbac7",
                brightBlack = "#636e7b", brightRed = "#ff938a", brightGreen = "#6bc46d", brightYellow = "#daaa3f", brightBlue = "#6cb6ff", brightMagenta = "#dcbdfb", brightCyan = "#56d4dd", brightWhite = "#cdd9e5",
            }
            : new
            {
                fontSize,
                background = "#fbfbfb",
                foreground = "#1f1f1f",
                cursor = "#000000",
                selectionBackground = "#3a5a8c40",
                black = "#24292f", red = "#cf222e", green = "#116329", yellow = "#4d2d00", blue = "#0969da", magenta = "#8250df", cyan = "#1b7c83", white = "#6e7781",
                brightBlack = "#57606a", brightRed = "#a40e26", brightGreen = "#1a7f37", brightYellow = "#633c01", brightBlue = "#218bff", brightMagenta = "#a475f9", brightCyan = "#3192aa", brightWhite = "#8c959f",
            };

        Post(new { type = "theme", theme });
    }

    private async void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var document = JsonDocument.Parse(args.WebMessageAsJson);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        switch (typeElement.GetString())
        {
            case "ready":
                _ready = true;
                PostTheme();
                foreach (var chunk in _pendingOut)
                {
                    Post(new { type = "out", data = chunk });
                }

                _pendingOut.Clear();
                Ready?.Invoke(this, EventArgs.Empty);
                break;

            case "in":
                if (root.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } text)
                {
                    _room?.SendRaw(text);
                }

                break;

            case "resize":
                if (root.TryGetProperty("cols", out var cols) && root.TryGetProperty("rows", out var rows))
                {
                    _room?.ResizeConsole(cols.GetInt32(), rows.GetInt32());
                }

                break;

            case "copy":
                if (root.TryGetProperty("text", out var copyText) && copyText.GetString() is { Length: > 0 } selected)
                {
                    var package = new DataPackage();
                    package.SetText(selected);
                    Clipboard.SetContent(package);
                }

                break;

            case "paste":
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                {
                    var pasted = await content.GetTextAsync();
                    Post(new { type = "paste", text = pasted });
                }

                break;

            case "title":
                if (root.TryGetProperty("title", out var title) && title.GetString() is { } t)
                {
                    TitleChanged?.Invoke(this, t);
                }

                break;

            default:
                break;
        }
    }
}

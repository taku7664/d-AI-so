using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Daiso.App.Services;
using Daiso.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Daiso.App.Terminal;

/// <summary>
/// 앱 안의 터미널 화면. WebView2에 동봉한 xterm.js(Assets/xterm)를 로컬 가상 호스트로 띄우고
/// <see cref="TerminalRoomViewModel"/>과 메시지로 잇는다. 네트워크는 쓰지 않는다. (ARCHITECTURE §5.3)
///
/// 앱 → 페이지: out(base64 UTF-8) · paste(text) · theme · focus · fit · reset · find · find-clear
/// 페이지 → 앱: in(키 입력) · resize(cols,rows) · copy(text) · paste(요청) · ready(cols,rows) · title · drop-files(파일 객체 동봉)
/// </summary>
public sealed class TerminalHost : UserControl
{
    private const string VirtualHost = "daiso.terminal";

    private readonly WebView2 _web = new();
    private readonly List<string> _pendingOut = [];
    private TerminalRoomViewModel? _room;
    private Action<byte[]>? _roomSink;
    private int _generation;
    private bool _ready;
    private bool _initialized;
    private int _cols;
    private int _rows;
    private ISettingsStore? _settings;

    public TerminalHost()
    {
        Content = _web;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ActualThemeChanged += (_, _) => PostTheme();

        // 설정(글자 크기)이 저장되면 열린 방에도 바로 반영한다. 화면을 떠나면 끊고 돌아오면 다시 건다
        Loaded += (_, _) =>
        {
            _settings ??= App.Services.GetRequiredService<ISettingsStore>();
            _settings.Changed -= OnSettingsChanged;
            _settings.Changed += OnSettingsChanged;
        };
        Unloaded += (_, _) =>
        {
            if (_settings is not null)
            {
                _settings.Changed -= OnSettingsChanged;
            }
        };
    }

    /// <summary>경로들을 입력 줄에 붙인다. 빈칸이 있으면 따옴표로 감싼다. 파일 첨부 버튼·끌어놓기·복사한 파일 붙이기가 다 이 길이다.</summary>
    public void PastePaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var list = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (list.Count > 0)
        {
            Post(new { type = "paste", text = QuotePaths(list) });
        }
    }

    /// <summary>
    /// 파일들을 CLI 에 준다. 그림 파일(png·jpg·gif·bmp·webp)은 클립보드에 DIB 로 올리고 그 도구의 그림 붙이기 키를 보내
    /// CLI 가 <c>[Image #1]</c>처럼 첨부하게 한다(그림마다 한 번씩, 사이에 잠깐 쉰다 — CLI 가 클립보드를 읽을 시간).
    /// 그 밖의 파일은 경로를 붙인다. 끌어놓기와 파일 첨부 버튼이 다 이 길이다. 사용자의 클립보드는 덮어써진다.
    /// </summary>
    public async Task AttachFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (_room is not { } room)
        {
            return;
        }

        var list = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        var images = list.Where(ClipboardImage.IsImageFile).ToList();
        var others = list.Except(images, StringComparer.OrdinalIgnoreCase).ToList();

        if (others.Count > 0)
        {
            PastePaths(others);
        }

        var provider = App.Services.GetRequiredService<IEnumerable<Daiso.Core.IProvider>>().First(candidate => candidate.Kind == room.Tool);
        foreach (var image in images)
        {
            if (await ClipboardImage.PutFileAsync(image))
            {
                room.SendRaw(provider.ImagePasteKeys);
                await Task.Delay(600);
            }
            else
            {
                PastePaths([image]);
            }
        }
    }

    /// <summary>
    /// CLI 의 입력 줄을 비운다. 입력 줄은 CLI 것이라 우리가 지울 수 없고 키만 보낼 수 있다:
    /// Ctrl+E(줄 끝으로) 뒤 Ctrl+U(줄 앞까지 지우기). Claude Code·Codex·Gemini 의 줄 편집기가 다 readline 꼴이라 통한다.
    /// 여러 줄로 이어 쓴 입력은 마지막 줄만 지워질 수 있다.
    /// </summary>
    public void ClearInput() => _room?.SendRaw("\x05\x15");

    /// <summary>xterm이 뜨고 첫 크기를 보고했다. 그 뒤부터 출력이 바로 그려진다.</summary>
    public event EventHandler? Ready;

    /// <summary>프로세스가 제목을 바꿨다(OSC 0). 방 제목(탭 툴팁)에 쓴다.</summary>
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

    /// <summary>WebView2를 만들고 xterm 페이지를 연다. 한 번만 하면 된다. 실패하면 다음 호출이 다시 시도한다.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
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

            // index.html 을 고쳤을 때 WebView2 디스크 캐시의 옛 페이지가 뜨지 않게 파일 시각을 붙인다
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(AssetFolder, "index.html")).Ticks;
            _web.Source = new Uri($"https://{VirtualHost}/index.html?v={stamp}");

            // 여기까지 와야 초기화된 것이다. 중간에 던지면 플래그가 안 서서 다음에 다시 시도한다
            _initialized = true;
        }
        catch
        {
            _initialized = false;
            throw;
        }
    }

    /// <summary>
    /// 방을 화면에 잇는다. xterm을 초기 상태로 되돌리고(모드·스크롤백까지) 그 방의 지난 화면을 재생한 뒤 이후 출력을 잇는다.
    /// 다른 방으로 바꾸면 이전 방 구독을 끊고 새 방을 되돌린다. 하나의 호스트를 탭마다 다시 가리켜 쓴다.
    /// 비활성 동안 창 크기가 바뀌었을 수 있어, 지금 xterm 크기를 그 방의 콘솔에 한 번 알려 준다.
    /// </summary>
    public void BindRoom(TerminalRoomViewModel room)
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

        // reset: clear()는 현재 줄과 대체 화면·bracketed paste 같은 모드를 남긴다. 다른 방의 버퍼를 깨끗한 상태 위에 재생해야 한다
        Post(new { type = "reset" });
        _pendingOut.Clear();
        SyncSize();
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

    /// <summary>지금 xterm 크기를 붙은 방의 콘솔에 알린다. ready·resize·BindRoom에서 부른다.</summary>
    private void SyncSize()
    {
        if (_cols > 0 && _rows > 0)
        {
            _room?.ResizeConsole(_cols, _rows);
        }
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

    private void OnSettingsChanged(object? sender, EventArgs e) => PostTheme();

    private void PostTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark
            || (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        _settings ??= App.Services.GetRequiredService<ISettingsStore>();
        var fontSize = Math.Clamp(_settings.Current.TerminalFontSize, 8, 28);

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

    /// <summary>
    /// 페이지에서 온 메시지. async void 핸들러라 여기서 던지면 앱이 죽는다. 클립보드는 다른 앱이 잡고 있으면
    /// COMException을 던지므로 감싼다. 그 밖의 처리는 던질 일이 없는 필드 읽기뿐이다.
    /// </summary>
    private async void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            await HandleWebMessageAsync(args.WebMessageAsJson, args.AdditionalObjects).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 클립보드 잠김·깨진 메시지. 한 번의 붙이기/복사가 안 된 것뿐이니 조용히 넘긴다
        }
    }

    private async Task HandleWebMessageAsync(string json, IReadOnlyList<object>? additionalObjects)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        switch (typeElement.GetString())
        {
            case "ready":
                _ready = true;
                ReadSize(root);
                PostTheme();
                SyncSize();
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
                ReadSize(root);
                SyncSize();
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
                await PasteFromClipboardAsync();
                break;

            // 페이지에 끌어다 놓은 파일. 경로를 붙여 넣으면 CLI 가 그 파일(그림 포함)을 본다
            case "drop-files":
                var paths = (additionalObjects ?? []).OfType<CoreWebView2File>().Select(file => file.Path).ToList();
                if (paths.Count > 0)
                {
                    Post(new { type = "paste", text = QuotePaths(paths) });
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

    private void ReadSize(JsonElement root)
    {
        if (root.TryGetProperty("cols", out var cols) && root.TryGetProperty("rows", out var rows)
            && cols.ValueKind == JsonValueKind.Number && rows.ValueKind == JsonValueKind.Number)
        {
            _cols = cols.GetInt32();
            _rows = rows.GetInt32();
        }
    }
    /// <summary>
    /// Ctrl+V. 클립보드가 글자면 우리가 붙여 넣는다(bracketed paste 는 xterm 이 감싼다).
    /// 파일(탐색기에서 복사)이면 경로를 붙여 넣는다. 그림만 있으면 Ctrl+V 키 자체를 CLI 에 넘긴다 —
    /// Claude Code 같은 CLI 는 그 키를 받으면 스스로 클립보드 그림을 읽으므로, 우리가 가로채면 그림 첨부가 막힌다.
    /// </summary>
    private async Task PasteFromClipboardAsync()
    {
        var content = Clipboard.GetContent();

        if (content.Contains(StandardDataFormats.Text))
        {
            Post(new { type = "paste", text = await content.GetTextAsync() });
            return;
        }

        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            var paths = items.Select(item => item.Path).Where(path => path.Length > 0).ToList();
            if (paths.Count > 0)
            {
                Post(new { type = "paste", text = QuotePaths(paths) });
                return;
            }
        }

        if (content.Contains(StandardDataFormats.Bitmap) && _room is { } room)
        {
            // 그림은 CLI 가 스스로 클립보드에서 읽는다. 그 키는 도구마다 달라 IProvider 가 안다 (ToolKind 로 분기하지 않는다, ARCHITECTURE §6.2)
            var provider = App.Services.GetRequiredService<IEnumerable<Daiso.Core.IProvider>>().First(candidate => candidate.Kind == room.Tool);
            room.SendRaw(provider.ImagePasteKeys);
        }
    }

    /// <summary>
    /// 경로를 CLI 입력에 붙일 꼴로. 빈칸이 있으면 따옴표로 감싸고, 여럿이면 빈칸으로 잇고, 앞뒤에 빈칸 하나를 둔다.
    /// 앞에도 두는 이유: CLI 가 붙인 글의 끝 빈칸을 지워 다음에 붙인 경로가 앞 경로에 들러붙었다.
    /// </summary>
    private static string QuotePaths(IEnumerable<string> paths) =>
        " " + string.Join(' ', paths.Select(path => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path)) + " ";

}

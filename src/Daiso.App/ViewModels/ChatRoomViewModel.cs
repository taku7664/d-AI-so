using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Infrastructure.Pty;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 방 하나 = 의사 콘솔 프로세스 + 그 세션 파일 tail + 채팅 블록. (ARCHITECTURE §5.3)
/// 방은 콘솔을 직접 쥐고 출력을 버퍼에 쌓는다. 그래서 화면(터미널 호스트)이 붙었다 떨어져도
/// 다시 붙을 때 그동안의 화면을 되돌려 줄 수 있다. 방은 <see cref="RoomManager"/>가 들고 있어 화면 이동에도 살아 있다.
/// </summary>
public sealed partial class ChatRoomViewModel : ObservableObject, IDisposable
{
    /// <summary>되돌리기용 출력 버퍼 상한. 넘으면 앞부분을 버린다(오래된 스크롤백). 8MB면 긴 세션도 화면 복원에 충분하다.</summary>
    private const int MaxBufferBytes = 8 * 1024 * 1024;

    private readonly DispatcherQueue _dispatcher;
    private readonly ToolKind _tool;
    private readonly object _bufferGate = new();
    private readonly LinkedList<byte[]> _buffer = new();
    private int _bufferBytes;

    private PtySession? _session;
    private SessionTail? _tail;
    private MessageRole? _lastRole;
    private bool _disposed;

    public ChatRoomViewModel(ToolKind tool, string projectDirectory, DispatcherQueue dispatcher)
    {
        _tool = tool;
        _dispatcher = dispatcher;
        ProjectDirectory = projectDirectory;
        Title = $"{ToolLook.Title(tool)} · {Formats.FolderName(projectDirectory)}";
    }

    public ToolKind Tool => _tool;

    public string ProjectDirectory { get; }

    public string Title { get; }

    /// <summary>탭에 다는 짧은 이름(폴더). 도구 아바타와 함께.</summary>
    public string ShortTitle => Formats.FolderName(ProjectDirectory);

    public string Avatar => ToolLook.Initial(_tool);

    public Microsoft.UI.Xaml.Media.Brush AvatarBrush => ToolLook.Brush(_tool);

    /// <summary>지금 따라가는 세션 파일. 실행 중 판정에 쓴다. 아직 못 찾았으면 null.</summary>
    public string? ActiveSessionFile => _tail?.ActiveFile;

    /// <summary>채팅 블록. 세션 파일 tail이 새 메시지를 붙일 때마다 늘어난다.</summary>
    public ObservableCollection<ChatBlockViewModel> Blocks { get; } = [];

    /// <summary>콘솔 출력 한 덩어리(base64 아닌 원시 바이트). 지금 붙은 터미널 호스트가 받는다. UI 스레드에서 온다.</summary>
    public event Action<byte[]>? OutputChunk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalVisibility))]
    [NotifyPropertyChangedFor(nameof(ChatVisibility))]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool showTerminal = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThinkingVisibility))]
    private bool isWaiting;

    [ObservableProperty]
    private string input = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool hasExited;

    public bool IsRunning => !HasExited;

    public Visibility TerminalVisibility => ShowTerminal ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChatVisibility => ShowTerminal ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ThinkingVisibility => IsWaiting && !ShowTerminal ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => Blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ToggleLabel => ShowTerminal
        ? UiStrings.Get("Room_ShowChat")
        : UiStrings.Get("Room_ShowTerminal");

    /// <summary>콘솔·tail을 방에 건다. 방이 출력 구독·버퍼를 맡는다.</summary>
    public void Bind(PtySession session, SessionTail tail)
    {
        _session = session;
        _tail = tail;

        session.OutputReceived += OnOutput;
        session.Exited += _ => _dispatcher.TryEnqueue(() => HasExited = true);
        tail.MessagesAppended += OnMessages;
        tail.Start();
    }

    /// <summary>지금까지 쌓인 화면을 새로 붙는 호스트에 되돌려 준다. 탭을 다시 보거나 화면을 다시 열 때.</summary>
    public void ReplayInto(Action<byte[]> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        byte[][] snapshot;
        lock (_bufferGate)
        {
            snapshot = [.. _buffer];
        }

        foreach (var chunk in snapshot)
        {
            sink(chunk);
        }
    }

    /// <summary>호스트가 키 입력을 방으로 보낸다.</summary>
    public void SendRaw(string text)
    {
        if (!_disposed && !HasExited)
        {
            _session?.Write(text);
        }
    }

    /// <summary>호스트가 창 크기를 방으로 알린다.</summary>
    public void ResizeConsole(int columns, int rows) => _session?.Resize(columns, rows);

    private void OnOutput(byte[] chunk)
    {
        lock (_bufferGate)
        {
            _buffer.AddLast(chunk);
            _bufferBytes += chunk.Length;

            while (_bufferBytes > MaxBufferBytes && _buffer.First is { } first)
            {
                _bufferBytes -= first.Value.Length;
                _buffer.RemoveFirst();
            }
        }

        _dispatcher.TryEnqueue(() => OutputChunk?.Invoke(chunk));
    }

    private void OnMessages(IReadOnlyList<SessionMessage> messages)
    {
        _dispatcher.TryEnqueue(() =>
        {
            foreach (var message in messages)
            {
                var showHeader = _lastRole != message.Role;
                _lastRole = message.Role;
                Blocks.Add(new ChatBlockViewModel(message, _tool, showHeader));
            }

            IsWaiting = messages.Count > 0 && Blocks.Count > 0 && Blocks[^1].Role == MessageRole.User;
            OnPropertyChanged(nameof(EmptyVisibility));
        });
    }

    /// <summary>입력 칸의 글을 콘솔로 보낸다. 줄 끝에 CR을 붙여 한 줄로 확정한다.</summary>
    [RelayCommand]
    private void Send()
    {
        if (_session is null || string.IsNullOrEmpty(Input) || HasExited)
        {
            return;
        }

        _session.Write(Input + "\r");
        Input = string.Empty;
    }

    [RelayCommand]
    private void ToggleView() => ShowTerminal = !ShowTerminal;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_session is not null)
        {
            _session.OutputReceived -= OnOutput;
        }

        if (_tail is not null)
        {
            _tail.MessagesAppended -= OnMessages;
            _tail.Dispose();
        }

        _session?.Dispose();

        lock (_bufferGate)
        {
            _buffer.Clear();
            _bufferBytes = 0;
        }
    }
}

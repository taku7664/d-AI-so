using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Infrastructure.Pty;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 터미널 방 하나 = 의사 콘솔 프로세스 + 되돌리기 버퍼 (+ 세션 파일 tail은 "안 본 답" 점에만 쓴다). (ARCHITECTURE §5.3)
/// 방은 콘솔을 직접 쥐고 출력을 <see cref="OutputReplayBuffer"/>에 쌓는다. 그래서 화면(터미널 호스트)이 붙었다 떨어져도
/// 다시 붙을 때 그동안의 화면을 되돌려 줄 수 있다. 방은 <see cref="RoomManager"/>가 들고 있어 화면 이동에도 살아 있다.
/// </summary>
public sealed partial class TerminalRoomViewModel : ObservableObject, IRoom
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ToolKind _tool;
    private readonly OutputReplayBuffer _buffer = new();
    private readonly string _baseTitle;

    private PtySession? _session;
    private SessionTail? _tail;
    private bool _disposed;

    public TerminalRoomViewModel(ToolKind tool, string projectDirectory, DispatcherQueue dispatcher)
    {
        _tool = tool;
        _dispatcher = dispatcher;
        ProjectDirectory = projectDirectory;
        _baseTitle = $"{ToolLook.Title(tool)} · {Formats.FolderName(projectDirectory)}";
        title = _baseTitle;
    }

    public ToolKind Tool => _tool;

    public string ProjectDirectory { get; }

    /// <summary>도구 · 폴더. 프로세스가 제목(OSC 0)을 보내면 뒤에 붙는다. 탭 툴팁에 쓴다.</summary>
    [ObservableProperty]
    private string title;

    /// <summary>탭에 다는 짧은 이름(폴더). 도구 아바타와 함께.</summary>
    public string ShortTitle => Formats.FolderName(ProjectDirectory);

    /// <summary>안의 프로그램이 바꾼 창 제목. 비어 있으면 도구·폴더만 보인다.</summary>
    public void SetProcessTitle(string? processTitle)
    {
        var trimmed = processTitle?.Trim();
        Title = string.IsNullOrEmpty(trimmed) ? _baseTitle : $"{_baseTitle} — {trimmed}";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool hasExited;

    public bool IsRunning => !HasExited;

    /// <summary>이 방이 지금 보고 있는 탭인가. 페이지가 정한다. 보고 있으면 새 답이 와도 표시하지 않는다.</summary>
    public bool IsActiveTab { get; private set; }

    /// <summary>안 본 새 답이 있는가. 다른 탭에 있는 동안 어시스턴트 답이 오면 켜진다. 탭 점으로 보인다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnseenVisibility))]
    private bool hasUnseen;

    public Visibility UnseenVisibility => HasUnseen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>이 탭을 보기 시작했다. 안 본 표시를 지운다.</summary>
    public void MarkActive()
    {
        IsActiveTab = true;
        HasUnseen = false;
    }

    /// <summary>다른 탭으로 옮겨 갔다.</summary>
    public void MarkInactive() => IsActiveTab = false;

    /// <summary>콘솔과 세션 tail을 방에 건다. 방이 출력 구독·버퍼를 맡는다. tail은 안 본 답 점에만 쓴다.</summary>
    public void Bind(PtySession session, SessionTail tail)
    {
        _session = session;
        _tail = tail;

        session.OutputReceived += OnOutput;
        session.Exited += _ => _dispatcher.TryEnqueue(() => HasExited = true);
        tail.MessagesAppended += OnMessages;
        tail.Start();
    }

    /// <summary>호스트를 이 방에 붙인다. 지금까지 쌓인 화면을 되돌리고, 그 뒤 출력을 이 호스트로 보낸다.</summary>
    public void AttachHost(Action<byte[]> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        foreach (var chunk in _buffer.Attach(sink))
        {
            sink(chunk);
        }
    }

    /// <summary>이 호스트가 지금 이 방을 그리는 호스트면 떼어 낸다. 다른 방으로 옮겨 갈 때.</summary>
    public void DetachHost(Action<byte[]> sink) => _buffer.Detach(sink);

    /// <summary>호스트가 키 입력을 방으로 보낸다.</summary>
    public void SendRaw(string text)
    {
        if (!_disposed && !HasExited)
        {
            _session?.Write(text);
        }
    }

    /// <summary>호스트가 창 크기를 방으로 알린다. 다시 붙을 때도 불러 비활성 중 바뀐 크기를 따라잡는다.</summary>
    public void ResizeConsole(int columns, int rows)
    {
        if (!_disposed && columns > 0 && rows > 0)
        {
            _session?.Resize(columns, rows);
        }
    }

    private void OnOutput(byte[] chunk)
    {
        // 버퍼 넣기와 싱크 잡기는 버퍼 안 한 잠금이다. AttachHost의 스냅샷과 겹쳐도 정확히 한 번만 간다
        if (_buffer.Append(chunk) is { } sink)
        {
            _dispatcher.TryEnqueue(() => sink(chunk));
        }
    }

    private void OnMessages(IReadOnlyList<SessionMessage> messages)
    {
        // 다른 탭을 보는 동안 어시스턴트 답이 오면 탭에 점을 켠다
        if (!IsActiveTab && messages.Any(message => message.Role == MessageRole.Assistant))
        {
            _dispatcher.TryEnqueue(() => HasUnseen = true);
        }
    }

    /// <summary>
    /// 방을 닫는다. 프로세스 트리는 지금 바로 끝내고(앱 종료 때도 고아가 안 남게), 읽기 루프가 빠져나오길 기다리는
    /// 핸들 정리는 백그라운드로 보낸다. UI 스레드가 최대 2초 굳던 것을 막는다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_tail is not null)
        {
            _tail.MessagesAppended -= OnMessages;
            _tail.Dispose();
        }

        if (_session is { } session)
        {
            session.OutputReceived -= OnOutput;
            session.Kill();
            _ = Task.Run(session.Dispose);
        }

        _buffer.Clear();
    }
}

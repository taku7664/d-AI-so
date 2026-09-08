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
/// 터미널(원시 화면)과 채팅(블록)은 같은 자리를 토글로 바꿔 쓴다. 입력 칸의 글은 콘솔로 들어간다.
/// 뷰가 <see cref="PtySession"/>과 <see cref="SessionTail"/>을 만들어 <see cref="Bind"/>로 연결한다.
/// </summary>
public sealed partial class ChatRoomViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ToolKind _tool;
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

    /// <summary>채팅 블록. 세션 파일 tail이 새 메시지를 붙일 때마다 늘어난다.</summary>
    public ObservableCollection<ChatBlockViewModel> Blocks { get; } = [];

    /// <summary>지금 터미널(원시 화면)을 보는가. false면 채팅.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalVisibility))]
    [NotifyPropertyChangedFor(nameof(ChatVisibility))]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool showTerminal = true;

    /// <summary>어시스턴트가 답하는 중(마지막이 사용자 말). 채팅에 "쓰는 중…"을 보인다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThinkingVisibility))]
    private bool isWaiting;

    [ObservableProperty]
    private string input = string.Empty;

    /// <summary>프로세스가 끝났다.</summary>
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

    /// <summary>뷰가 만든 콘솔·tail을 방에 건다.</summary>
    public void Bind(PtySession session, SessionTail tail)
    {
        _session = session;
        _tail = tail;

        session.Exited += _ => _dispatcher.TryEnqueue(() => HasExited = true);
        tail.MessagesAppended += OnMessages;
        tail.Start();
    }

    private void OnMessages(IReadOnlyList<SessionMessage> messages)
    {
        _dispatcher.TryEnqueue(() =>
        {
            foreach (var message in messages)
            {
                // 화자가 바뀌는 첫 블록만 머리(아바타·이름·시각)를 단다
                var showHeader = _lastRole != message.Role;
                _lastRole = message.Role;
                Blocks.Add(new ChatBlockViewModel(message, _tool, showHeader));
            }

            // 마지막이 사용자 말이면 답을 기다리는 중
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

        if (_tail is not null)
        {
            _tail.MessagesAppended -= OnMessages;
            _tail.Dispose();
        }

        _session?.Dispose();
    }
}

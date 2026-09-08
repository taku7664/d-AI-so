using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Daiso.Core.Chat;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Daiso.App.ViewModels;

/// <summary>
/// 방 하나 = 챗봇 세션(<see cref="IChatSession"/>) + 말풍선. 터미널 없이 CLI를 대화로 다룬다. (FEATURE_PLAN B 모드)
/// 엔진 사건을 받아 말풍선을 만들고 자라게 한다. 스트리밍 답은 한 말풍선에 글자를 이어 붙인다.
/// </summary>
public sealed partial class StreamingRoomViewModel : ObservableObject, IRoom
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ToolKind _tool;
    private readonly Dictionary<string, ChatBubbleViewModel> _toolCards = new(StringComparer.Ordinal);

    private IChatSession? _session;
    private ChatBubbleViewModel? _openAssistant;
    private bool _disposed;

    public StreamingRoomViewModel(ToolKind tool, string projectDirectory, DispatcherQueue dispatcher)
    {
        _tool = tool;
        _dispatcher = dispatcher;
        ProjectDirectory = projectDirectory;
        Title = $"{ToolLook.Title(tool)} · {Formats.FolderName(projectDirectory)}";
    }

    public ToolKind Tool => _tool;

    public string ProjectDirectory { get; }

    public string Title { get; }

    public string ShortTitle => Formats.FolderName(ProjectDirectory);

    public string Avatar => ToolLook.Initial(_tool);

    public Microsoft.UI.Xaml.Media.Brush AvatarBrush => ToolLook.Brush(_tool);

    /// <summary>대화 말풍선.</summary>
    public ObservableCollection<ChatBubbleViewModel> Bubbles { get; } = [];

    /// <summary>이 방에서 부를 수 있는 슬래시 명령. 방을 열 때 채운다.</summary>
    private IReadOnlyList<Daiso.Core.Prompts.SlashCommand> _commands = [];

    public ObservableCollection<Daiso.Core.Prompts.SlashCommand> Suggestions { get; } = [];

    public void SetCommands(IReadOnlyList<Daiso.Core.Prompts.SlashCommand> commands) => _commands = commands;

    public void FilterSuggestions(string? text)
    {
        Suggestions.Clear();

        if (string.IsNullOrEmpty(text) || text[0] != '/')
        {
            return;
        }

        var query = text[1..].TrimStart();
        foreach (var command in _commands.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(12))
        {
            Suggestions.Add(command);
        }
    }

    [ObservableProperty]
    private string input = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThinkingVisibility))]
    private bool isWaiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool hasExited;

    public bool IsRunning => !HasExited;

    public bool IsActiveTab { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnseenVisibility))]
    private bool hasUnseen;

    public Visibility UnseenVisibility => HasUnseen ? Visibility.Visible : Visibility.Collapsed;

    public void MarkActive()
    {
        IsActiveTab = true;
        HasUnseen = false;
    }

    public void MarkInactive() => IsActiveTab = false;

    public Visibility ThinkingVisibility => IsWaiting ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => Bubbles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>엔진을 방에 건다.</summary>
    public void Bind(IChatSession session)
    {
        _session = session;
        session.Event += OnEvent;
        session.Exited += code => _dispatcher.TryEnqueue(() => OnExited(code));
    }

    private void OnEvent(ChatEvent chatEvent)
    {
        _dispatcher.TryEnqueue(() => Apply(chatEvent));
    }

    private void OnExited(int code)
    {
        HasExited = true;
        IsWaiting = false;
        _openAssistant = null;

        // 답 없이 끝났으면(=엔진이 곧장 죽음) 사람이 원인을 알 수 있게 알린다
        Bubbles.Add(ChatBubbleViewModel.ForTool(UiStrings.Format("Room_SessionEnded", code), _tool));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private void Apply(ChatEvent chatEvent)
    {
        switch (chatEvent)
        {
            case ChatEvent.AssistantDelta delta:
                EnsureAssistant().Append(delta.Text);
                IsWaiting = false;
                break;

            case ChatEvent.AssistantMessage message:
                // 스트리밍으로 이미 채운 말풍선이 있으면 그걸 닫고(중복 방지), 없으면 통째로 새 말풍선
                if (_openAssistant is { } open && open.Text.Length > 0)
                {
                    _openAssistant = null;
                }
                else
                {
                    var bubble = EnsureAssistant();
                    if (bubble.Text.Length == 0)
                    {
                        bubble.Append(message.Text);
                    }

                    _openAssistant = null;
                }

                IsWaiting = false;
                break;

            case ChatEvent.ToolUse tool:
                _openAssistant = null;
                var card = ChatBubbleViewModel.ForTool(tool.Summary, _tool);
                _toolCards[tool.Id] = card;
                Bubbles.Add(card);
                break;

            case ChatEvent.ToolResult result:
                if (_toolCards.TryGetValue(result.Id, out var toolCard))
                {
                    toolCard.AttachResult(result.Summary, result.IsError);
                }

                break;

            case ChatEvent.PermissionRequest permission when _session is not null:
                _openAssistant = null;
                Bubbles.Add(ChatBubbleViewModel.ForPermission(
                    permission.RequestId, permission.ToolName, permission.Summary, _tool, _session.Respond));
                MarkUnseenIfBackground();
                break;

            case ChatEvent.TurnEnded ended:
                _openAssistant = null;
                IsWaiting = false;
                if (ended.IsError && ended.Message is { Length: > 0 } errorText)
                {
                    Bubbles.Add(ChatBubbleViewModel.ForTool(UiStrings.Format("Room_TurnError", errorText), _tool));
                }

                MarkUnseenIfBackground();
                break;

            default:
                break;
        }

        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private ChatBubbleViewModel EnsureAssistant()
    {
        if (_openAssistant is null)
        {
            _openAssistant = ChatBubbleViewModel.ForAssistant(_tool);
            Bubbles.Add(_openAssistant);
        }

        return _openAssistant;
    }

    private void MarkUnseenIfBackground()
    {
        if (!IsActiveTab)
        {
            HasUnseen = true;
        }
    }

    /// <summary>사용자 메시지를 보낸다.</summary>
    [RelayCommand]
    private void Send()
    {
        if (_session is null || string.IsNullOrWhiteSpace(Input) || HasExited)
        {
            return;
        }

        Bubbles.Add(ChatBubbleViewModel.ForUser(Input, _tool));
        _session.Send(Input);
        Input = string.Empty;
        IsWaiting = true;
        _openAssistant = null;
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_session is not null)
        {
            _session.Event -= OnEvent;
            _session.Dispose();
        }
    }
}

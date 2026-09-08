using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.ViewModels;

/// <summary>말풍선의 갈래.</summary>
public enum ChatBubbleKind
{
    User,
    Assistant,
    Tool,
    Permission,
}

/// <summary>
/// 챗봇 대화의 말풍선 하나. 스트리밍 답은 <see cref="Text"/>에 조각을 이어 붙여 글자 단위로 자란다. (FEATURE_PLAN B 모드)
/// 도구 호출은 접힌 카드, 승인 요청은 허용/거부 버튼이 붙은 카드.
/// </summary>
public sealed partial class ChatBubbleViewModel : ObservableObject
{
    private readonly ToolKind _tool;
    private readonly Action<string, bool>? _respond;
    private readonly string? _requestId;

    private ChatBubbleViewModel(ChatBubbleKind kind, ToolKind tool, string text, string? requestId, Action<string, bool>? respond)
    {
        Kind = kind;
        _tool = tool;
        Text = text;
        _requestId = requestId;
        _respond = respond;
    }

    public ChatBubbleKind Kind { get; }

    /// <summary>지금까지의 글. 스트리밍이면 계속 자란다.</summary>
    [ObservableProperty]
    private string text;

    public string TimeText { get; } = DateTime.Now.ToString("HH:mm");

    /// <summary>사용자 말풍선.</summary>
    public static ChatBubbleViewModel ForUser(string text, ToolKind tool) =>
        new(ChatBubbleKind.User, tool, text, null, null);

    /// <summary>어시스턴트 말풍선. 스트리밍으로 자란다.</summary>
    public static ChatBubbleViewModel ForAssistant(ToolKind tool) =>
        new(ChatBubbleKind.Assistant, tool, string.Empty, null, null);

    /// <summary>도구 호출 카드.</summary>
    public static ChatBubbleViewModel ForTool(string summary, ToolKind tool) =>
        new(ChatBubbleKind.Tool, tool, summary, null, null);

    /// <summary>승인 요청 카드. 허용/거부가 세션으로 답을 돌려준다.</summary>
    public static ChatBubbleViewModel ForPermission(string requestId, string toolName, string summary, ToolKind tool, Action<string, bool> respond) =>
        new(ChatBubbleKind.Permission, tool, summary, requestId, respond) { PermissionTool = toolName };

    /// <summary>스트리밍 조각을 이어 붙인다.</summary>
    public void Append(string chunk) => Text += chunk;

    /// <summary>도구 결과를 카드 아래에 붙인다.</summary>
    public void AttachResult(string summary, bool isError)
    {
        ResultText = summary;
        ResultIsError = isError;
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultVisibility));
    }

    public string PermissionTool { get; private init; } = string.Empty;

    public string ResultText { get; private set; } = string.Empty;

    public bool ResultIsError { get; private set; }

    public bool HasResult => ResultText.Length > 0;

    public Visibility ResultVisibility => HasResult ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>승인 카드가 아직 답을 기다리는가. 답하면 버튼이 사라지고 결과가 남는다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingVisibility))]
    [NotifyPropertyChangedFor(nameof(DecidedVisibility))]
    private bool decided;

    [ObservableProperty]
    private string decisionText = string.Empty;

    public string Avatar => ToolLook.Initial(_tool);

    public Brush AvatarBrush => ToolLook.Brush(_tool);

    public string SpeakerText => Kind == ChatBubbleKind.Assistant ? ToolLook.Title(_tool) : UiStrings.Get("Role_User");

    public Visibility UserVisibility => Kind == ChatBubbleKind.User ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AssistantVisibility => Kind == ChatBubbleKind.Assistant ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ToolVisibility => Kind == ChatBubbleKind.Tool ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PermissionVisibility => Kind == ChatBubbleKind.Permission ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PendingVisibility => Kind == ChatBubbleKind.Permission && !Decided ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DecidedVisibility => Decided ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private void Allow() => Decide(true);

    [RelayCommand]
    private void Deny() => Decide(false);

    private void Decide(bool allow)
    {
        if (Decided || _requestId is null || _respond is null)
        {
            return;
        }

        _respond(_requestId, allow);
        Decided = true;
        DecisionText = UiStrings.Get(allow ? "Room_PermissionAllowed" : "Room_PermissionDenied");
    }
}

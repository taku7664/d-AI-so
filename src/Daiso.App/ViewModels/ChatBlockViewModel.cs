using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.ViewModels;

/// <summary>
/// 채팅방의 말풍선 하나. 세션 메시지 한 건을 디스코드식 블록으로 보여준다. (ARCHITECTURE §5.3)
/// 같은 화자가 연속으로 말하면 <see cref="ShowHeader"/>가 false가 되어 아바타·이름 없이 이어 붙는다.
/// 도구 호출은 접힌 회색 블록, 사용자 말은 왼쪽 강조 세로줄, 시각은 호버 때 오른쪽에.
/// </summary>
public sealed partial class ChatBlockViewModel : ObservableObject
{
    private const int PreviewChars = 1500;

    private readonly ToolKind _tool;

    public ChatBlockViewModel(SessionMessage message, ToolKind tool, bool showHeader)
    {
        Message = message;
        _tool = tool;
        ShowHeader = showHeader;
        FullText = message.Text;
    }

    public SessionMessage Message { get; }

    public string FullText { get; }

    /// <summary>화자가 바뀌는 첫 블록만 아바타·이름·시각 머리를 단다.</summary>
    public bool ShowHeader { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    private bool isExpanded;

    public MessageRole Role => Message.Role;

    /// <summary>도구 아바타 글자(C·X·G). 어시스턴트 블록만 보인다.</summary>
    public string Avatar => ToolLook.Initial(_tool);

    public Brush AvatarBrush => ToolLook.Brush(_tool);

    public string RoleText => UiStrings.Get(Message.Role switch
    {
        MessageRole.User => "Role_User",
        MessageRole.Assistant => "Role_Assistant",
        MessageRole.System => "Role_System",
        _ => "Role_Tool",
    });

    /// <summary>도구 이름(어시스턴트) 또는 역할 이름. 머리에 굵게.</summary>
    public string SpeakerText => Message.Role == MessageRole.Assistant ? ToolLook.Title(_tool) : RoleText;

    public string TimeText => $"{Message.At.ToLocalTime():HH:mm}";

    /// <summary>호버 시 시각 툴팁. 전체 날짜.</summary>
    public string FullTimeText => Message.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public bool IsUser => Message.Role == MessageRole.User;

    public bool IsToolOrSystem => Message.Role is MessageRole.Tool or MessageRole.System;

    public bool IsPlain => !IsToolOrSystem;

    public Visibility UserBarVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AvatarVisibility =>
        ShowHeader && Message.Role == MessageRole.Assistant ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HeaderVisibility => ShowHeader ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PlainVisibility => IsPlain ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CollapsibleVisibility => IsToolOrSystem ? Visibility.Visible : Visibility.Collapsed;

    public bool IsTruncated => FullText.Length > PreviewChars;

    public Visibility MoreVisibility => IsTruncated ? Visibility.Visible : Visibility.Collapsed;

    public string DisplayText => IsExpanded || !IsTruncated
        ? FullText
        : FullText[..PreviewChars] + " …";

    public string MoreText => IsExpanded
        ? UiStrings.Get("Sessions_ShowLess")
        : UiStrings.Format("Sessions_ShowMore", FullText.Length - PreviewChars);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

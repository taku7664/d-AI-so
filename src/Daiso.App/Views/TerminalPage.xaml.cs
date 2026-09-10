using Windows.ApplicationModel.DataTransfer;
using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Daiso.App.Views;

/// <summary>
/// 터미널 화면. 위는 탭 띠(새 터미널 + 열린 방), 아래는 새 세션 카드 또는 그 방의 터미널.
/// 페이지는 캐시된다(NavigationCacheMode=Required). Loaded는 돌아올 때마다 다시 돌므로 한 번만 걸 것은 생성자에 둔다. (ARCHITECTURE §5.3)
/// </summary>
public sealed partial class TerminalPage : Page, IPageHeaderSource, IFileDropSink
{
    private IRoom? _room;

    public TerminalPage()
    {
        InitializeComponent();
        FocusRelease.Attach(this);
        ViewModel = App.Services.GetRequiredService<TerminalViewModel>();
        Header = new PageHeader("Terminal_Title", UiStrings.Get("Terminal_Subtitle"));

        RoomTabs.ItemsSource = App.Rooms.Rooms;

        // 뒤로/앞으로가 등록부의 방을 바꾸면 화면도 그 방으로 간다. 이미 그 방이면 아무것도 하지 않아 되돌이가 안 생긴다
        App.Rooms.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ViewModels.RoomManager.ActiveRoom) || ReferenceEquals(App.Rooms.ActiveRoom, _room))
            {
                return;
            }

            if (App.Rooms.ActiveRoom is { } target && App.Rooms.Rooms.Contains(target))
            {
                SelectRoom(target);
            }
            else if (App.Rooms.ActiveRoom is null)
            {
                ShowNewSession();
            }
        };
        // 터미널 안에서 Ctrl+F. WebView2 가 포커스를 잡으면 앱 단축키가 안 닿아 이 길로 온다
        Embedded.FindRequested += OnTerminalFindRequested;

        // 새 터미널 카드에서 단계가 열리면 그 단계로 부드럽게 굴리고 포커스를 옮긴다
        ViewModel.StepRevealRequested += OnStepRevealRequested;

        Embedded.TitleChanged += (_, title) =>
        {
            if (_room is TerminalRoomViewModel terminal)
            {
                terminal.SetProcessTitle(title);
                RoomTitleText.Text = terminal.Title;
            }
        };

        Loaded += async (_, _) =>
        {
            SyncTabFromViewModel();
            ViewModel.LoadPromptChoices();
            await ViewModel.RefreshInstalledAsync();
            RestoreRooms();

            // 세션 "이어서 열기"가 방을 바로 열어 달라고 했으면 새 터미널 방을 연다
            if (ViewModel.ConsumeAutoOpen())
            {
                _ = OpenTerminalRoomAsync();
            }
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TerminalViewModel.SelectedToolIndex):
                    SyncTabFromViewModel();
                    break;
                case nameof(TerminalViewModel.WorkingDirectory):
                case nameof(TerminalViewModel.FolderExpanded):
                    // 접혀 있던 ComboBox 는 Loaded 가 늦어 글이 비어 보인다. 펼칠 때 다시 맞춘다
                    SyncFolderBox();
                    break;
                default:
                    break;
            }
        };
        FolderBox.Loaded += (_, _) => SyncFolderBox();
    }

    /// <summary>뷰모델의 폴더를 콤보박스 글에 맞춘다(아는 프로젝트·이어서 열기·최근 목록 갱신 뒤).</summary>
    private void SyncFolderBox()
    {
        var value = ViewModel.WorkingDirectory ?? string.Empty;

        // 목록에 있는 값은 SelectedItem 으로 잡아야 편집형 ComboBox 가 글을 보여 준다.
        // Text 만 넣으면 SelectedItem 이 null 인 채라 자리표시자("폴더 경로")가 그대로 남는다
        var match = ViewModel.FolderChoices.FirstOrDefault(
            path => string.Equals(path, value, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            if (!ReferenceEquals(FolderBox.SelectedItem, match))
            {
                FolderBox.SelectedItem = match;
            }

            return;
        }

        FolderBox.SelectedItem = null;

        if (!string.Equals(FolderBox.Text, value, StringComparison.Ordinal))
        {
            FolderBox.Text = value;
        }
    }

    /// <summary>직접 친 경로(Enter 또는 포커스 이동).</summary>
    private void OnFolderTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        ViewModel.SetFolder(args.Text.Trim());
        args.Handled = true;
    }

    /// <summary>최근 폴더 목록에서 고름.</summary>
    private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderBox.SelectedItem is string path && !string.Equals(path, ViewModel.WorkingDirectory, StringComparison.Ordinal))
        {
            ViewModel.SetFolder(path);
        }
    }

    /// <summary>좌측 하단 알림 줄. 클립보드처럼 눈에 안 보이는 일의 결과를 여기 적는다 (docs/REVIEW_BACKLOG.md B3)</summary>
    private ShellViewModel Shell { get; } = App.Services.GetRequiredService<ShellViewModel>();

    public TerminalViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    // ── 새 세션 카드 ───────────────────────────────────────────────────────

    private async void OnPickFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        // unpackaged 앱은 피커에 창 핸들을 직접 붙여야 한다.
        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            ViewModel.SetFolder(folder.Path);
        }
    }

    /// <summary>카드를 누르면 뷰모델의 도구가 바뀌고, 1단계가 끝나 다음 단계가 열린다.</summary>
    private void OnToolCardChanged(object sender, SelectionChangedEventArgs e)
    {
        PaintCards();

        if (sender is ListView { SelectedIndex: >= 0 } cards)
        {
            ViewModel.ChooseTool(cards.SelectedIndex);
        }
    }

    // ── 카드 채움 ─────────────────────────────────────────────────────────
    //
    // AI 카드·새로/이어서 카드의 고름·호버 채움은 목록 컨테이너가 아니라 카드(Border)가 칠한다.
    // WinUI 의 ListViewItemPresenter 는 채움을 위아래 2px 안쪽에 모서리 없이 그려서 둥근 테두리 카드와 어긋났다 —
    // 테두리는 둥근데 채움은 각지고, 위쪽에 안 칠해진 띠가 남았다. 색은 목록의 기본 브러시를 그대로 쓴다.

    private readonly List<Border> _cards = new();

    private Border? _hoveredCard;

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border card)
        {
            if (!_cards.Contains(card))
            {
                _cards.Add(card);
            }

            PaintCard(card);
        }
    }

    private void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            _hoveredCard = card;
            PaintCard(card);
        }
    }

    private void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            if (ReferenceEquals(_hoveredCard, card))
            {
                _hoveredCard = null;
            }

            PaintCard(card);
        }
    }

    /// <summary>선택이 바뀌면 카드 전부를 다시 칠한다. 화면에서 내려간 카드는 잊는다.</summary>
    private void PaintCards()
    {
        _cards.RemoveAll(card => !card.IsLoaded);

        foreach (var card in _cards)
        {
            PaintCard(card);
        }
    }

    private void PaintCard(Border card)
    {
        DependencyObject? node = card;

        while (node is not null and not ListViewItem)
        {
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        // 색은 WinUI 목록 기본값 그대로다(generic.xaml): 고름·호버 = SubtleFillColorSecondary, 고른 것 위 호버 = SubtleFillColorTertiary.
        // 리소스 키로 꺼내지 않고 ActualTheme 로 고르는 이유: 페이지·앱 리소스로 풀면 창 테마가 아니라 앱 기본 테마 값이 나온다
        // (다크 창에서 라이트 값 #09000000 이 나와 카드가 오히려 어두워졌다). UsagePage 의 줄 밝힘과 같은 방식이다.
        // 배경을 비우지 않고 투명으로 두는 이유: Border 는 배경이 없으면 안쪽이 히트테스트되지 않아 호버가 글자 위에서만 뜬다
        var selected = node is ListViewItem { IsSelected: true };
        var hovered = ReferenceEquals(_hoveredCard, card);
        var dark = card.ActualTheme == ElementTheme.Dark;

        var color = (selected, hovered) switch
        {
            (true, true) => dark ? Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x06, 0, 0, 0),
            (true, false) or (false, true) => dark ? Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x09, 0, 0, 0),
            _ => Microsoft.UI.Colors.Transparent,
        };

        card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }

    // ── 단계 굴리기 ───────────────────────────────────────────────────────
    // 단계 머리는 누르는 곳이 아니다. 단계는 답을 따라서만 열리고, 한 번 열리면 닫히지 않는다

    /// <summary>열린 단계 머리를 맨 위에서 이만큼 띄워 둔다. 딱 붙으면 위 단계와의 경계가 안 보인다.</summary>
    private const double StepRevealGap = 8;

    /// <summary>
    /// 굴림이 끝나면 포커스를 줄 칸. 굴리는 도중에 포커스를 주면 WinUI 가 그 자리로 곧장 튀어 부드러움이 깨지므로
    /// 굴림이 멈춘 뒤(<see cref="OnStepsScrollViewChanged"/>)에 준다.
    /// </summary>
    private Control? _pendingStepFocus;

    /// <summary>
    /// 뷰모델이 단계를 열었다. 그 단계 머리를 맨 위로 부드럽게 굴리고, 멈추면 <b>그 단계에서 답할 칸</b>에 포커스를 둔다.
    /// 방금 보이게 된 몸이 배치된 뒤에 목표 위치를 재야 맞으므로 한 박자 늦춘다.
    /// </summary>
    private void OnStepRevealRequested(object? sender, TerminalStep step)
    {
        FrameworkElement header = step switch
        {
            TerminalStep.Tool => StepToolHeader,
            TerminalStep.Folder => StepFolderHeader,
            _ => StepSessionHeader,
        };

        Control answer = step switch
        {
            TerminalStep.Tool => ToolCards,
            TerminalStep.Folder => FolderBox,
            _ => SessionModeRadios,
        };

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RevealStep(header, answer));
    }

    private void RevealStep(FrameworkElement header, Control answer)
    {
        if (!header.IsLoaded || NewSessionPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        StepsGrid.UpdateLayout();

        var top = header.TransformToVisual(StepsGrid).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        var target = Math.Clamp(top - StepRevealGap, 0, StepsScroll.ScrollableHeight);

        // 이미 그 자리면 굴림이 일어나지 않아 ViewChanged 가 오지 않는다. 그때는 바로 포커스
        if (Math.Abs(target - StepsScroll.VerticalOffset) < 1)
        {
            _pendingStepFocus = null;
            answer.Focus(FocusState.Programmatic);
            return;
        }

        _pendingStepFocus = answer;
        StepsScroll.ChangeView(null, target, null, disableAnimation: false);
    }

    private void OnStepsScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate && _pendingStepFocus is { } answer)
        {
            _pendingStepFocus = null;
            answer.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>새로 시작 / 이어서를 고른다. 안 고른 상태는 SelectedIndex = -1 이라 여기로 안 온다.</summary>
    private void OnSessionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        PaintCards();

        if (sender is ListView { SelectedIndex: >= 0 } cards)
        {
            ViewModel.ChooseSessionMode(cards.SelectedIndex);
        }
    }

    /// <summary>이어서 열기처럼 뷰모델이 도구를 바꾸면 카드 선택도 따라간다.</summary>
    private void SyncTabFromViewModel()
    {
        // 아직 아무 AI도 안 고른 화면이면 카드를 켜지 않는다. 여기서 켜면 1단계가 저절로 끝나 버린다
        ToolCards.SelectedIndex = ViewModel.ToolSelection;
    }

    /// <summary>실행될 명령을 클립보드에 넣는다. 붙여넣으면 그대로 돌아가는 한 줄이어야 한다.</summary>
    private void OnCopyPreviewClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.Preview);
        Clipboard.SetContent(package);

        if (sender is Button button)
        {
            ToolTipService.SetToolTip(button, UiStrings.Get("Common_Copied"));
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            ViewModel.AppendPresetCommand.Execute(preset);
        }
    }

    /// <summary>
    /// 모델 칸에서 고른 줄을 인자 칸에 반영한다.
    /// <para>
    /// 선택은 한 방향(<c>OneWay</c>)으로만 묶고 여기서 받는다. 탭을 바꾸면 콤보가 목록을 갈아 끼우며 선택을 비우는데,
    /// 양방향으로 묶으면 그 빈 값이 새 탭의 모델을 지운다. 다른 탭의 줄이 오면 뷰모델이 알아보고 무시한다.
    /// </para>
    /// </summary>
    private void OnModelPicked(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ModelChoiceViewModel choice)
        {
            ViewModel.SelectedTool.PickModel(choice);
        }
    }

    /// <summary>새 세션 카드의 "규칙 편집": 이 폴더를 내 규칙 화면에 넘기고 그 화면으로 간다.</summary>
    private void OnEditRulesClick(object sender, RoutedEventArgs e) => GoToRules(ViewModel.WorkingDirectory);

    private static void GoToRules(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            App.Services.GetRequiredService<RuleMakerViewModel>().ProjectDirectory = directory;
        }

        (App.MainWindow as ShellWindow)?.NavigateTo("RuleMaker");
    }

    // ── 탭 띠: 새 터미널 / 방 ──────────────────────────────────────────────

    /// <summary>이 화면에 들어오면 이미 열려 있던 방이 있으면 마지막 방을, 없으면 새 터미널 탭을 보인다.</summary>
    private void RestoreRooms()
    {
        if (_room is not null && App.Rooms.Rooms.Contains(_room))
        {
            ShowRoom(_room);
        }
        else if (App.Rooms.Rooms.Count > 0)
        {
            SelectRoom(App.Rooms.Rooms[^1]);
        }
        else
        {
            ShowNewSession();
        }
    }

    /// <summary>새 터미널 탭을 누르면 새 세션 카드. 열린 방은 그대로 살아 있다.</summary>
    private void OnNewTabClick(object sender, RoutedEventArgs e) => ShowNewSession();

    private void ShowNewSession()
    {
        _room?.MarkInactive();
        _room = null;
        App.Rooms.ActiveRoom = null;
        RoomTabs.SelectedItem = null;
        NewTab.IsChecked = true;
        RoomCard.Visibility = Visibility.Collapsed;
        RoomTools.Visibility = Visibility.Collapsed;
        NewSessionPanel.Visibility = Visibility.Visible;
    }

    /// <summary>그 방을 화면에 보인다. 방 종류에 맞는 화면(챗봇 말풍선 / 터미널)을 켠다.</summary>
    private void SelectRoom(IRoom room)
    {
        if (ReferenceEquals(_room, room))
        {
            App.Rooms.ActiveRoom = room;
            ShowRoom(room);
            return;
        }

        if (_room is StreamingRoomViewModel previousChat)
        {
            previousChat.Bubbles.CollectionChanged -= OnBubblesChanged;
        }

        _room?.MarkInactive();
        _room = room;
        room.MarkActive();
        App.Rooms.ActiveRoom = room;

        if (room is StreamingRoomViewModel chat)
        {
            chat.Bubbles.CollectionChanged += OnBubblesChanged;
        }

        ShowRoom(room);
    }

    private void ShowRoom(IRoom room)
    {
        NewTab.IsChecked = false;
        NewSessionPanel.Visibility = Visibility.Collapsed;
        RoomTabs.SelectedItem = room;
        RoomCard.DataContext = room;
        RoomCard.Visibility = Visibility.Visible;

        if (room is StreamingRoomViewModel chat)
        {
            ChatbotPanel.DataContext = chat;
            ChatbotPanel.Visibility = Visibility.Visible;
            TerminalPanel.Visibility = Visibility.Collapsed;
            RoomTools.Visibility = Visibility.Collapsed;
            RoomInput.Focus(FocusState.Programmatic);
            ScrollChatToEnd();
        }
        else if (room is TerminalRoomViewModel terminal)
        {
            TerminalPanel.DataContext = terminal;
            TerminalPanel.Visibility = Visibility.Visible;
            ChatbotPanel.Visibility = Visibility.Collapsed;
            RoomTools.Visibility = Visibility.Visible;
            RoomTitleText.Text = terminal.Title;
            _ = BindTerminalAsync(terminal);
        }
    }

    /// <summary>
    /// 찾기 아이콘: 도구 줄을 찾기 모드로 바꾼다.
    /// <para>
    /// 옆에 덧붙이지 않고 <b>평소 줄을 대신한다</b> — 덧붙이면 창 하한에서 줄이 넘쳐 오른쪽이 잘렸다.
    /// </para>
    /// </summary>
    private void OnFindToggleClick(object sender, RoutedEventArgs e) =>
        SetFindMode(FindToggle.IsChecked == true);

    /// <summary>찾기 줄의 닫기 단추.</summary>
    private void OnFindCloseClick(object sender, RoutedEventArgs e) => SetFindMode(false);

    /// <summary>터미널 안에서 Ctrl+F 를 눌렀다. 이미 열려 있으면 입력칸으로 포커스만 옮긴다.</summary>
    private void OnTerminalFindRequested(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => SetFindMode(true));

    private void SetFindMode(bool on)
    {
        FindToggle.IsChecked = on;
        FindPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RoomToolIcons.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

        if (on)
        {
            FindBox.Focus(FocusState.Programmatic);
            FindBox.SelectAll();
        }
        else
        {
            FindBox.Text = string.Empty;
            Embedded.ClearFind();
            Embedded.FocusTerminal();
        }
    }

    /// <summary>도구 줄의 폴더 열기: 이 방의 프로젝트 폴더를 탐색기로.</summary>
    private async void OnRoomOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (_room is TerminalRoomViewModel room && Directory.Exists(room.ProjectDirectory))
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(room.ProjectDirectory);
        }
    }

    /// <summary>도구 줄의 "같은 폴더로 새 터미널": 새 세션 카드에 이 방의 도구·폴더를 채워 보인다.</summary>
    private void OnRoomDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (_room is TerminalRoomViewModel room)
        {
            ViewModel.SelectTool(room.Tool);
            ViewModel.SetFolder(room.ProjectDirectory);
            ViewModel.SessionModeIndex = 0;
            ShowNewSession();
        }
    }

    private async Task BindTerminalAsync(TerminalRoomViewModel room)
    {
        try
        {
            await Embedded.InitializeAsync();
            Embedded.BindRoom(room);
            Embedded.FocusTerminal();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            ShowRoomNote(ex.Message);
        }
    }

    private void OnRoomTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomTabs.SelectedItem is IRoom room && !ReferenceEquals(room, _room))
        {
            SelectRoom(room);
        }
    }

    /// <summary>탭의 X. 그 방을 닫고 프로세스를 끝낸다. 보던 방이면 남은 마지막 방(없으면 새 터미널)을 보인다.</summary>
    private void OnCloseRoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IRoom room })
        {
            return;
        }

        var wasSelected = ReferenceEquals(room, _room);

        if (room is StreamingRoomViewModel chat)
        {
            chat.Bubbles.CollectionChanged -= OnBubblesChanged;
        }

        App.Rooms.Close(room);

        if (!wasSelected)
        {
            return;
        }

        _room = null;

        if (App.Rooms.Rooms.Count > 0)
        {
            SelectRoom(App.Rooms.Rooms[^1]);
        }
        else
        {
            ShowNewSession();
        }
    }

    // ── 방 열기 ───────────────────────────────────────────────────────────

    // 챗봇 방(B 모드, stream-json 말풍선). 지금은 UI 비공개 — 여는 버튼이 없어 이 핸들러는 호출되지 않는다. 재공개하려면 버튼을 되살린다. (ARCHITECTURE §5.3)
    private void OnOpenChatbotClick(object sender, RoutedEventArgs e) => OpenChatbotRoom();

    private void OnOpenTerminalClick(object sender, RoutedEventArgs e) => _ = OpenTerminalRoomAsync();

    /// <summary>고른 도구·폴더. 폴더가 없으면 안내하고 null.</summary>
    private (ToolLaunchViewModel Tool, string Directory)? RoomTarget()
    {
        var tool = ViewModel.SelectedTool;

        if (tool is null || !ViewModel.CanLaunch)
        {
            ShowRoomNote(UiStrings.Get("Terminal_PickFolderFirst"));
            return null;
        }

        return (tool, ViewModel.WorkingDirectory!);
    }

    /// <summary>챗봇 방: Claude를 stream-json으로. 말풍선 대화. 지금은 Claude만.</summary>
    private void OpenChatbotRoom()
    {
        if (RoomTarget() is not { } target)
        {
            return;
        }

        var (tool, directory) = target;

        if (tool.Provider.Kind != Daiso.Core.ToolKind.Claude)
        {
            ShowRoomNote(UiStrings.Get("Room_ClaudeOnly"));
            return;
        }

        try
        {
            var extra = string.IsNullOrWhiteSpace(tool.Arguments) ? string.Empty : " " + tool.Arguments.Trim();
            var claudeArgs = "--print --output-format stream-json --input-format stream-json --include-partial-messages --verbose --dangerously-skip-permissions" + extra;
            // 이름("claude")만 넘기면 방금 깐 도구를 셸이 못 찾는다. 제공자가 주는 실행 위치를 쓴다 (ARCHITECTURE LaunchTarget 규칙)
            var launch = tool.Provider.LaunchTarget;
            var quoted = launch.Contains(' ', StringComparison.Ordinal) ? $"\"{launch}\"" : launch;
            var session = Daiso.Infrastructure.Chat.ClaudeChatSession.Start("cmd.exe", $"/c {quoted} {claudeArgs}", directory);

            var room = new StreamingRoomViewModel(tool.Provider.Kind, directory, DispatcherQueue);
            room.SetCommands(App.Services.GetRequiredService<Daiso.Infrastructure.SlashCommandReader>().Read(tool.Provider.Kind, directory));
            room.Bind(session);
            AddAndSelect(room);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ShowRoomNote(ex.Message);
        }
    }

    /// <summary>
    /// 터미널 방: 진짜 터미널(xterm + 의사 콘솔). 인자는 뷰모델이 합친다(resume + 사용자 인자 + 프롬프트 시작 메시지).
    /// 미설치면 설치 흐름, WebView2가 없으면 외부 터미널로.
    /// </summary>
    private async Task OpenTerminalRoomAsync()
    {
        if (!ViewModel.SelectedTool.IsInstalled || !Terminal.TerminalHost.IsRuntimeAvailable())
        {
            await ViewModel.LaunchAsync(ViewModel.SelectedTool);
            return;
        }

        if (RoomTarget() is not { } target)
        {
            return;
        }

        var (tool, directory) = target;

        try
        {
            await Embedded.InitializeAsync();

            var arguments = ViewModel.ComposeArguments(tool, writePrompt: true);
            var builder = new Daiso.Infrastructure.TerminalCommandBuilder(Daiso.Providers.Common.ExecutableLocator.ExistsOnPath);
            var shell = builder.BuildShellCommand(tool.Provider.LaunchTarget, arguments);
            var session = Daiso.Infrastructure.Pty.PtySession.Start($"{shell.FileName} {shell.Arguments}", directory);
            var tail = new Daiso.Infrastructure.SessionTail(tool.Provider, directory, DateTimeOffset.Now);

            var room = new TerminalRoomViewModel(tool.Provider.Kind, directory, DispatcherQueue);
            room.Bind(session, tail);
            ViewModel.LastCommand = $"{directory} > {tool.Provider.LaunchTarget} {arguments}".TrimEnd();
            AddAndSelect(room);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowRoomNote(ex.Message);
        }
    }

    private void AddAndSelect(IRoom room)
    {
        App.Rooms.Add(room);
        EmbeddedStatus.Visibility = Visibility.Collapsed;
        SelectRoom(room);
    }

    /// <summary>방을 못 열었거나 붙이지 못했다. 새 세션 카드의 실행 칸 아래에 사람 말로 알린다.</summary>
    private void ShowRoomNote(string text)
    {
        EmbeddedStatus.Text = text;
        EmbeddedStatus.Visibility = Visibility.Visible;
        ShowNewSession();
    }

    // ── 방 도구 줄: 찾기 · 프롬프트 · 규칙 ─────────────────────────────────

    /// <summary>찾기 칸: 글이 바뀌면 다음 것을 찾고, 비우면 표시를 지운다.</summary>
    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindBox.Text))
        {
            Embedded.ClearFind();
        }
        else
        {
            Embedded.Find(FindBox.Text, previous: false);
        }
    }

    /// <summary>Enter 다음, Shift+Enter 이전, Esc는 비우고 터미널로 포커스.</summary>
    private void OnFindKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter:
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                Embedded.Find(FindBox.Text, previous: shift);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Escape:
                SetFindMode(false);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void OnFindPrevClick(object sender, RoutedEventArgs e)
    {
        if (FindBox.Text.Length > 0)
        {
            Embedded.Find(FindBox.Text, previous: true);
        }
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e)
    {
        if (FindBox.Text.Length > 0)
        {
            Embedded.Find(FindBox.Text, previous: false);
        }
    }

    /// <summary>
    /// 방의 "프롬프트 넣기": 내 프롬프트 목록. 이 방 폴더에 이미 넣은 것은 체크로 보인다.
    /// 누르면 그 폴더의 docs/prompts에 써 넣고 시작 메시지를 터미널 입력에 타이핑해 준다(Enter는 사람이 친다).
    /// </summary>
    private void OnRoomPromptFlyoutOpening(object? sender, object e)
    {
        RoomPromptFlyout.Items.Clear();

        if (_room is not TerminalRoomViewModel room)
        {
            return;
        }

        var library = App.Services.GetRequiredService<Daiso.Core.IPromptLibrary>();
        var directory = room.ProjectDirectory;

        var prompts = Daiso.Core.BuiltInPrompts.List()
            .Concat(library.List())
            .GroupBy(prompt => prompt.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(prompt => prompt.Name, StringComparer.CurrentCulture)
            .ToList();

        foreach (var prompt in prompts)
        {
            var applied = File.Exists(Path.Combine(directory, "docs", "prompts", prompt.Id + ".md"));
            var item = new MenuFlyoutItem { Text = prompt.Name, Icon = applied ? new SymbolIcon(Symbol.Accept) : null };

            ToolTipService.SetToolTip(item, applied
                ? UiStrings.Get("Terminal_PromptApplied")
                : UiStrings.Format("Terminal_PromptApply", prompt.Name));

            var captured = prompt;
            item.Click += (_, _) => ApplyPromptToRoom(room, captured);
            RoomPromptFlyout.Items.Add(item);
        }

        if (RoomPromptFlyout.Items.Count == 0)
        {
            RoomPromptFlyout.Items.Add(new MenuFlyoutItem { Text = UiStrings.Get("Terminal_NoPrompts"), IsEnabled = false });
        }
    }

    // ── 끌어놓기 (IFileDropSink — 셸의 FileDropTarget 이 OLE 드롭을 받아 넘긴다) ───────────

    public bool CanAcceptFiles => TerminalPanel.Visibility == Visibility.Visible;

    /// <summary>파일 드래그가 창에 들어왔다. 방이 보이면 터미널 위에 받는 판을 덮는다 (WebView2 위로는 XAML 이 안 그려지므로 그 위 판이 표시 역할).</summary>
    public void FileDragStarted()
    {
        if (TerminalPanel.Visibility == Visibility.Visible)
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    public void FileDragEnded() => DropOverlay.Visibility = Visibility.Collapsed;

    /// <summary>방이 있을 때만 받는다. 파일을 CLI 에 준다(그림은 첨부, 나머지는 경로). 방이 없으면 아무 일도 하지 않는다.</summary>
    public async Task FilesDroppedAsync(IReadOnlyList<string> paths)
    {
        if (TerminalPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        Embedded.FocusTerminal();
        await Embedded.AttachFilesAsync(paths);
    }

    /// <summary>
    /// 화면 글자를 클립보드에 담는다. 색 코드가 아니라 xterm 이 그린 글자라 그대로 붙여넣을 수 있다.
    /// 결과는 좌측 하단 알림 줄에 적는다 — 클립보드는 눈에 보이지 않아 됐는지 알 길이 없다.
    /// </summary>
    private async void OnRoomCopyScreenClick(object sender, RoutedEventArgs e)
    {
        var text = await Embedded.ReadScreenTextAsync();

        if (text.Length == 0)
        {
            Shell.Report(UiStrings.Get("Room_ScreenEmpty"));
            return;
        }

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            Shell.Report(UiStrings.Format("Room_ScreenCopied", text.Count(c => c == '\n') + 1));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // 다른 앱이 클립보드를 잡고 있다. 한 번의 복사가 안 된 것뿐이다
            Shell.Report(ex.Message);
        }
    }

    /// <summary>화면 글자를 텍스트 파일로 저장한다.</summary>
    private async void OnRoomSaveScreenClick(object sender, RoutedEventArgs e)
    {
        if (_room is not TerminalRoomViewModel room)
        {
            return;
        }

        var text = await Embedded.ReadScreenTextAsync();

        if (text.Length == 0)
        {
            Shell.Report(UiStrings.Get("Room_ScreenEmpty"));
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{Formats.FolderName(room.ProjectDirectory)}-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("Text", [".txt"]);

        // unpackaged 앱은 피커에 창 핸들을 직접 붙여야 한다.
        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(file.Path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Shell.Report(UiStrings.Format("Room_ScreenSaved", file.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Shell.Report(ex.Message);
        }
    }

    /// <summary>파일을 골라 CLI 에 준다. 여러 개 고를 수 있다. 그림은 첨부, 나머지는 경로 (AttachFilesAsync).</summary>
    private async void OnRoomAttachClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // unpackaged 앱은 피커에 창 핸들을 직접 붙여야 한다.
        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        var files = await picker.PickMultipleFilesAsync();
        Embedded.FocusTerminal();
        if (files.Count > 0)
        {
            await Embedded.AttachFilesAsync(files.Select(file => file.Path));
        }
    }

    /// <summary>CLI 입력 줄을 비운다 (키 전송).</summary>
    private void OnRoomClearInputClick(object sender, RoutedEventArgs e)
    {
        Embedded.ClearInput();
        Embedded.FocusTerminal();
    }

    private void ApplyPromptToRoom(TerminalRoomViewModel room, Daiso.Core.PromptPreset prompt)
    {
        try
        {
            App.Services.GetRequiredService<Daiso.Core.IPromptLibrary>().WriteIntoProject(prompt, room.ProjectDirectory);
            room.SendRaw(Daiso.Core.PromptPresetSerializer.StarterMessage(prompt));
            Embedded.FocusTerminal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ShowRoomNote(ex.Message);
        }
    }

    /// <summary>방의 "규칙 편집": 이 방의 폴더를 내 규칙 화면에 넘기고 그 화면으로 간다. 방은 살아 있다.</summary>
    private void OnRoomRulesClick(object sender, RoutedEventArgs e)
    {
        if (_room is TerminalRoomViewModel room)
        {
            GoToRules(room.ProjectDirectory);
        }
    }

    // ── 챗봇 방 입력(UI 비공개, 코드 유지) ────────────────────────────────

    private void OnBubblesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScrollChatToEnd();

    /// <summary>입력이 바뀌면 `/` 명령 제안을 다시 채운다(챗봇 방에서만).</summary>
    private void OnRoomInputTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && _room is StreamingRoomViewModel chat)
        {
            chat.FilterSuggestions(sender.Text);
        }
    }

    /// <summary>제안을 고르면 그 명령을 입력에 채운다(바로 보내지 않는다).</summary>
    private void OnRoomSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Daiso.Core.Prompts.SlashCommand command)
        {
            sender.Text = command.Invocation + " ";
        }
    }

    /// <summary>Enter로 제출하면 보낸다(챗봇 방). 제안을 고른 경우는 채우기만 한다.</summary>
    private void OnRoomQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_room is not StreamingRoomViewModel chat || args.ChosenSuggestion is not null)
        {
            return;
        }

        chat.Input = sender.Text;
        chat.SendCommand.Execute(null);
        sender.Text = string.Empty;
    }

    private void ScrollChatToEnd() =>
        DispatcherQueue.TryEnqueue(() => ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, disableAnimation: true));
}

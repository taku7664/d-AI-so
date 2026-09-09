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
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);
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
        Embedded.TitleChanged += (_, title) =>
        {
            if (_room is TerminalRoomViewModel terminal)
            {
                terminal.SetProcessTitle(title);
                RoomTitleText.Text = terminal.Title;
            }
        };

        // 도구 탭에 제작사 로고. 탭 순서 = ViewModel.Tools 순서(ToolLook.DisplayOrder)
        for (var i = 0; i < ToolTabs.Items.Count && i < ViewModel.Tools.Count; i++)
        {
            ToolTabs.Items[i].Icon = ToolLook.LogoIcon(ViewModel.Tools[i].Kind);
        }

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

    public TerminalViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    // ── 새 세션 카드 ───────────────────────────────────────────────────────

    /// <summary>앱이 아는 프로젝트를 목록으로 보여준다. 목록에 없는 폴더만 "다른 폴더 고르기"로 간다.</summary>
    private async void OnProjectFlyoutOpening(object? sender, object e)
    {
        await ViewModel.LoadProjectChoicesAsync();

        ProjectFlyout.Items.Clear();

        var labels = Formats.Labels([.. ViewModel.ProjectChoices]);

        for (var i = 0; i < ViewModel.ProjectChoices.Count; i++)
        {
            var path = ViewModel.ProjectChoices[i];
            var item = new MenuFlyoutItem { Text = labels[i], Icon = new SymbolIcon(Symbol.Folder) };

            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => ViewModel.SetFolder(path);
            ProjectFlyout.Items.Add(item);
        }

        if (ProjectFlyout.Items.Count == 0)
        {
            ProjectFlyout.Items.Add(new MenuFlyoutItem { Text = UiStrings.Get("Common_NoKnownProjects"), IsEnabled = false });
        }

        ProjectFlyout.Items.Add(new MenuFlyoutSeparator());

        var browse = new MenuFlyoutItem { Text = UiStrings.Get("Common_BrowseOther") };
        browse.Click += (_, _) => OnPickFolderClick(browse, new RoutedEventArgs());
        ProjectFlyout.Items.Add(browse);
    }

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

    /// <summary>탭을 누르면 뷰모델의 도구가 바뀐다.</summary>
    private void OnToolTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);

        if (index >= 0 && index != ViewModel.SelectedToolIndex)
        {
            ViewModel.SelectedToolIndex = index;
        }
    }

    /// <summary>이어서 열기처럼 뷰모델이 탭을 바꾸면 탭 띠도 따라간다.</summary>
    private void SyncTabFromViewModel()
    {
        if (ViewModel.SelectedToolIndex >= 0 && ViewModel.SelectedToolIndex < ToolTabs.Items.Count
            && !ReferenceEquals(ToolTabs.SelectedItem, ToolTabs.Items[ViewModel.SelectedToolIndex]))
        {
            ToolTabs.SelectedItem = ToolTabs.Items[ViewModel.SelectedToolIndex];
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            ViewModel.AppendPresetCommand.Execute(preset);
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

    /// <summary>도구 줄의 찾기 아이콘: 켜면 입력칸이 나오고 포커스, 끄면 표시를 지우고 터미널로 포커스.</summary>
    private void OnFindToggleClick(object sender, RoutedEventArgs e)
    {
        var on = FindToggle.IsChecked == true;
        FindPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (on)
        {
            FindBox.Focus(FocusState.Programmatic);
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
            var session = Daiso.Infrastructure.Chat.ClaudeChatSession.Start("cmd.exe", $"/c claude {claudeArgs}", directory);

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
            var shell = builder.BuildShellCommand(tool.Provider.ExecutableName, arguments);
            var session = Daiso.Infrastructure.Pty.PtySession.Start($"{shell.FileName} {shell.Arguments}", directory);
            var tail = new Daiso.Infrastructure.SessionTail(tool.Provider, directory, DateTimeOffset.Now);

            var room = new TerminalRoomViewModel(tool.Provider.Kind, directory, DispatcherQueue);
            room.Bind(session, tail);
            ViewModel.LastCommand = $"{directory} > {tool.Provider.ExecutableName} {arguments}".TrimEnd();
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
                FindToggle.IsChecked = false;
                OnFindToggleClick(FindToggle, new RoutedEventArgs());
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

    /// <summary>파일 드래그가 창에 들어왔다. 방이 보이면 터미널 위에 받는 판을 덮는다 (WebView2 위로는 XAML 이 안 그려지므로 그 위 판이 표시 역할).</summary>
    public void FileDragStarted()
    {
        if (TerminalPanel.Visibility == Visibility.Visible)
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    public void FileDragEnded() => DropOverlay.Visibility = Visibility.Collapsed;

    /// <summary>방이 있으면 파일을 CLI 에 준다(그림은 첨부, 나머지는 경로). 없으면 첫 폴더를 프로젝트 폴더로 잡는다.</summary>
    public async Task FilesDroppedAsync(IReadOnlyList<string> paths)
    {
        if (TerminalPanel.Visibility == Visibility.Visible)
        {
            Embedded.FocusTerminal();
            await Embedded.AttachFilesAsync(paths);
        }
        else if (paths.FirstOrDefault(Directory.Exists) is { } folder)
        {
            ViewModel.SetFolder(folder);
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

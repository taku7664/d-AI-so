using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Daiso.App.Views;

public sealed partial class TerminalPage : Page
{
    public TerminalPage()
    {
        InitializeComponent();
        FocusRelease.Attach(this);
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);
        ViewModel = App.Services.GetRequiredService<TerminalViewModel>();

        Loaded += async (_, _) =>
        {
            SyncTabFromViewModel();
            await ViewModel.RefreshInstalledAsync();
            RestoreRooms();

            // 세션 "이어서 열기"가 방을 바로 열어 달라고 했으면 새 방을 연다. Claude면 챗봇, 아니면 터미널
            if (ViewModel.ConsumeAutoOpen())
            {
                if (ViewModel.SelectedTool?.Provider.Kind == Daiso.Core.ToolKind.Claude)
                {
                    OpenChatbotRoom();
                }
                else
                {
                    _ = OpenTerminalRoomAsync();
                }
            }
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalViewModel.SelectedToolIndex))
            {
                SyncTabFromViewModel();
            }
        };
    }

    public TerminalViewModel ViewModel { get; }

    /// <summary>
    /// 앱이 아는 프로젝트를 목록으로 보여준다. 세션 인덱스·최근 폴더에 있는 폴더라
    /// 대화상자를 열 필요가 없다. 목록에 없는 폴더만 "다른 폴더 고르기"로 간다.
    /// </summary>
    private async void OnProjectFlyoutOpening(object? sender, object e)
    {
        await ViewModel.LoadProjectChoicesAsync();

        ProjectFlyout.Items.Clear();

        var labels = Formats.Labels([.. ViewModel.ProjectChoices]);

        for (var i = 0; i < ViewModel.ProjectChoices.Count; i++)
        {
            var path = ViewModel.ProjectChoices[i];
            var item = new MenuFlyoutItem
            {
                Text = labels[i],
                Icon = new SymbolIcon(Symbol.Folder),
            };

            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => ViewModel.SetFolder(path);
            ProjectFlyout.Items.Add(item);
        }

        if (ProjectFlyout.Items.Count == 0)
        {
            ProjectFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = UiStrings.Get("Common_NoKnownProjects"),
                IsEnabled = false,
            });
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

    private void OnRecentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: string path })
        {
            ViewModel.SetFolder(path);
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

    private IRoom? _room;

    /// <summary>이 화면에 처음 들어오면 이미 열려 있던 방들을 탭으로 되돌린다.</summary>
    private void RestoreRooms()
    {
        RoomTabs.ItemsSource = App.Rooms.Rooms;

        if (App.Rooms.Rooms.Count > 0)
        {
            RoomTabs.Visibility = Visibility.Visible;
            SelectRoom(App.Rooms.Rooms[^1]);
        }
    }

    private void OnOpenChatbotClick(object sender, RoutedEventArgs e) => OpenChatbotRoom();

    private void OnOpenTerminalClick(object sender, RoutedEventArgs e) => _ = OpenTerminalRoomAsync();

    /// <summary>고른 도구·폴더. 폴더가 없으면 안내하고 null.</summary>
    private (ToolLaunchViewModel Tool, string Directory)? RoomTarget()
    {
        var tool = ViewModel.SelectedTool;
        var directory = string.IsNullOrWhiteSpace(ViewModel.WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : ViewModel.WorkingDirectory;

        if (tool is null || !Directory.Exists(directory))
        {
            ShowRoomNote(UiStrings.Get("Terminal_PickFolderFirst"));
            return null;
        }

        return (tool, directory);
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

    /// <summary>터미널 방: 진짜 터미널(xterm + 의사 콘솔). 모든 도구. WebView2 없으면 외부 터미널로.</summary>
    private async Task OpenTerminalRoomAsync()
    {
        if (!Terminal.TerminalHost.IsRuntimeAvailable())
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

            var builder = new Daiso.Infrastructure.TerminalCommandBuilder(Daiso.Providers.Common.ExecutableLocator.ExistsOnPath);
            var shell = builder.BuildShellCommand(tool.Provider.ExecutableName, tool.Arguments);
            var session = Daiso.Infrastructure.Pty.PtySession.Start($"{shell.FileName} {shell.Arguments}", directory);
            var tail = new Daiso.Infrastructure.SessionTail(tool.Provider, directory, DateTimeOffset.Now);

            var room = new ChatRoomViewModel(tool.Provider.Kind, directory, DispatcherQueue);
            room.SetCommands(App.Services.GetRequiredService<Daiso.Infrastructure.SlashCommandReader>().Read(tool.Provider.Kind, directory));
            room.Bind(session, tail);
            AddAndSelect(room);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            ShowRoomNote(ex.Message);
        }
    }

    private void AddAndSelect(IRoom room)
    {
        App.Rooms.Add(room);
        RoomTabs.ItemsSource = App.Rooms.Rooms;
        RoomTabs.Visibility = Visibility.Visible;
        EmbeddedStatus.Visibility = Visibility.Collapsed;
        SelectRoom(room);
    }

    private void ShowRoomNote(string text)
    {
        EmbeddedStatus.Text = text;
        EmbeddedStatus.Visibility = Visibility.Visible;
    }

    /// <summary>그 방을 화면에 보인다. 방 종류에 맞는 화면(챗봇 말풍선 / 터미널)을 켠다.</summary>
    private void SelectRoom(IRoom room)
    {
        if (_room is StreamingRoomViewModel previousChat)
        {
            previousChat.Bubbles.CollectionChanged -= OnBubblesChanged;
        }

        _room?.MarkInactive();
        _room = room;
        room.MarkActive();

        RoomTabs.SelectedItem = room;
        RoomCard.DataContext = room;
        RoomCard.Visibility = Visibility.Visible;

        if (room is StreamingRoomViewModel chat)
        {
            ChatbotPanel.DataContext = chat;
            ChatbotPanel.Visibility = Visibility.Visible;
            TerminalPanel.Visibility = Visibility.Collapsed;
            chat.Bubbles.CollectionChanged += OnBubblesChanged;
            RoomInput.Focus(FocusState.Programmatic);
            ScrollChatToEnd();
        }
        else if (room is ChatRoomViewModel terminal)
        {
            TerminalPanel.DataContext = terminal;
            TerminalPanel.Visibility = Visibility.Visible;
            ChatbotPanel.Visibility = Visibility.Collapsed;
            _ = BindTerminalAsync(terminal);
        }
    }

    private async Task BindTerminalAsync(ChatRoomViewModel room)
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

    private void OnBubblesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScrollChatToEnd();

    private void OnRoomTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomTabs.SelectedItem is IRoom room && !ReferenceEquals(room, _room))
        {
            SelectRoom(room);
        }
    }

    /// <summary>탭의 X. 그 방을 닫고 프로세스를 끝낸다. 남은 방이 있으면 마지막 것을 보인다.</summary>
    private void OnCloseRoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: IRoom room })
        {
            return;
        }

        var wasSelected = ReferenceEquals(room, _room);
        App.Rooms.Close(room);

        if (App.Rooms.Rooms.Count == 0)
        {
            _room = null;
            RoomTabs.Visibility = Visibility.Collapsed;
            RoomCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (wasSelected)
        {
            SelectRoom(App.Rooms.Rooms[^1]);
        }
    }

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

    /// <summary>
    /// 내 프롬프트 목록을 연다. 각 프롬프트가 지금 작업 폴더의 프로젝트에 적용됐는지(docs/prompts/&lt;id&gt;.md 있는지)
    /// 표시하고, 누르면 그 폴더에 써 넣는다. (사용자 요청: 터미널에서 적용 여부 보기)
    /// </summary>
    private void OnPromptFlyoutOpening(object? sender, object e)
    {
        PromptFlyout.Items.Clear();

        var library = App.Services.GetRequiredService<Daiso.Core.IPromptLibrary>();
        var directory = ViewModel.WorkingDirectory;
        var hasFolder = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);

        var prompts = Daiso.Core.BuiltInPrompts.List()
            .Concat(library.List())
            .GroupBy(prompt => prompt.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(prompt => prompt.Name, StringComparer.CurrentCulture)
            .ToList();

        foreach (var prompt in prompts)
        {
            var applied = hasFolder && File.Exists(Path.Combine(directory!, "docs", "prompts", prompt.Id + ".md"));
            var item = new MenuFlyoutItem
            {
                Text = prompt.Name,
                Icon = applied ? new SymbolIcon(Symbol.Accept) : null,
            };

            ToolTipService.SetToolTip(item, applied
                ? UiStrings.Get("Terminal_PromptApplied")
                : UiStrings.Format("Terminal_PromptApply", prompt.Name));

            var captured = prompt;
            item.Click += (_, _) => ApplyPromptToProject(captured);
            PromptFlyout.Items.Add(item);
        }

        if (PromptFlyout.Items.Count == 0)
        {
            PromptFlyout.Items.Add(new MenuFlyoutItem { Text = UiStrings.Get("Terminal_NoPrompts"), IsEnabled = false });
        }
    }

    /// <summary>고른 프롬프트를 작업 폴더의 docs/prompts에 써 넣는다.</summary>
    private void ApplyPromptToProject(Daiso.Core.PromptPreset prompt)
    {
        var directory = ViewModel.WorkingDirectory;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            EmbeddedStatus.Text = UiStrings.Get("Terminal_PickFolderFirst");
            EmbeddedStatus.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var path = App.Services.GetRequiredService<Daiso.Core.IPromptLibrary>()
                .WriteIntoProject(prompt, directory);
            EmbeddedStatus.Text = UiStrings.Format("Terminal_PromptWritten", path);
            EmbeddedStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            EmbeddedStatus.Text = ex.Message;
            EmbeddedStatus.Visibility = Visibility.Visible;
        }
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            ViewModel.AppendPresetCommand.Execute(preset);
        }
    }
}

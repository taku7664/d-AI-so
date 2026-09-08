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

            // 세션 "이어서 열기"가 방을 바로 열어 달라고 했으면 새 방을 연다
            if (ViewModel.ConsumeAutoOpen())
            {
                await OpenRoomAsync();
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

    private ChatRoomViewModel? _room;

    /// <summary>이 화면에 처음 들어오면 이미 열려 있던 방들을 탭으로 되돌린다.</summary>
    private void RestoreRooms()
    {
        RoomTabs.ItemsSource = App.Rooms.Rooms;

        if (App.Rooms.Rooms.Count > 0)
        {
            RoomTabs.Visibility = Visibility.Visible;
            _ = SelectRoomAsync(App.Rooms.Rooms[^1]);
        }
    }

    private void OnEmbeddedOpenClick(object sender, RoutedEventArgs e) => _ = OpenRoomAsync();

    /// <summary>고른 도구로 새 방을 연다. 의사 콘솔 + 세션 tail을 만들어 탭에 더하고 그 방을 보인다.</summary>
    private async Task OpenRoomAsync()
    {
        var settings = App.Services.GetRequiredService<Services.ISettingsStore>().Current;

        // 설정이 껐거나 WebView2가 없으면 외부 터미널로 연다
        if (!settings.UseEmbeddedTerminal || !Terminal.TerminalHost.IsRuntimeAvailable())
        {
            await ViewModel.LaunchAsync(ViewModel.SelectedTool);
            return;
        }

        var tool = ViewModel.SelectedTool;
        var directory = string.IsNullOrWhiteSpace(ViewModel.WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : ViewModel.WorkingDirectory;

        if (tool is null || !Directory.Exists(directory))
        {
            EmbeddedStatus.Text = UiStrings.Get("Terminal_PickFolderFirst");
            EmbeddedStatus.Visibility = Visibility.Visible;
            return;
        }

        var builder = new Daiso.Infrastructure.TerminalCommandBuilder(Daiso.Providers.Common.ExecutableLocator.ExistsOnPath);
        var shell = builder.BuildShellCommand(tool.Provider.ExecutableName, tool.Arguments);
        var commandLine = $"{shell.FileName} {shell.Arguments}";

        try
        {
            await Embedded.InitializeAsync();

            var session = Daiso.Infrastructure.Pty.PtySession.Start(commandLine, directory);
            var tail = new Daiso.Infrastructure.SessionTail(tool.Provider, directory, DateTimeOffset.Now);

            var room = new ChatRoomViewModel(tool.Provider.Kind, directory, DispatcherQueue);
            room.SetCommands(App.Services.GetRequiredService<Daiso.Infrastructure.SlashCommandReader>().Read(tool.Provider.Kind, directory));
            room.Bind(session, tail);
            App.Rooms.Add(room);

            RoomTabs.ItemsSource = App.Rooms.Rooms;
            RoomTabs.Visibility = Visibility.Visible;
            EmbeddedStatus.Visibility = Visibility.Collapsed;

            await SelectRoomAsync(room);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            EmbeddedStatus.Text = ex.Message;
            EmbeddedStatus.Visibility = Visibility.Visible;
        }
    }

    /// <summary>그 방을 화면에 보인다. 하나뿐인 터미널 호스트를 그 방으로 다시 가리키고(버퍼 되돌림) 채팅·입력을 잇는다.</summary>
    private async Task SelectRoomAsync(ChatRoomViewModel room)
    {
        if (_room is not null)
        {
            _room.Blocks.CollectionChanged -= OnBlocksChanged;
            _room.MarkInactive();
        }

        _room = room;
        room.MarkActive();
        room.Blocks.CollectionChanged += OnBlocksChanged;

        RoomCard.DataContext = room;
        RoomCard.Visibility = Visibility.Visible;
        RoomTabs.SelectedItem = room;

        try
        {
            await Embedded.InitializeAsync();
            Embedded.BindRoom(room);

            // 채팅이 기본이면 입력칸으로, 터미널을 보고 있으면 터미널로 포커스
            if (room.ShowTerminal)
            {
                Embedded.FocusTerminal();
            }
            else
            {
                RoomInput.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            // WebView2를 못 띄웠다. 방(프로세스)은 살아 있으니 채팅으로 두고 안내만 한다
            EmbeddedStatus.Text = ex.Message;
            EmbeddedStatus.Visibility = Visibility.Visible;
        }

        ScrollChatToEnd();
    }

    private void OnBlocksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScrollChatToEnd();

    /// <summary>찾기 칸에서 Enter는 다음, Shift+Enter는 이전.</summary>
    private void OnRoomFindKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        var shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        Embedded.Find(RoomFindBox.Text, previous: shift);
    }

    private void OnRoomFindTextChanged(object sender, TextChangedEventArgs e)
    {
        if (RoomFindBox.Text.Length == 0)
        {
            Embedded.ClearFind();
        }
    }

    private void OnRoomFindNextClick(object sender, RoutedEventArgs e) => Embedded.Find(RoomFindBox.Text, previous: false);

    private void OnRoomFindPrevClick(object sender, RoutedEventArgs e) => Embedded.Find(RoomFindBox.Text, previous: true);

    private async void OnRoomTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoomTabs.SelectedItem is ChatRoomViewModel room && !ReferenceEquals(room, _room))
        {
            await SelectRoomAsync(room);
        }
    }

    /// <summary>탭의 X. 그 방을 닫고 프로세스를 끝낸다. 남은 방이 있으면 마지막 것을 보인다.</summary>
    private async void OnCloseRoomClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatRoomViewModel room })
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
            await SelectRoomAsync(App.Rooms.Rooms[^1]);
        }
    }

    /// <summary>입력이 바뀌면 `/` 명령 제안을 다시 채운다.</summary>
    private void OnRoomInputTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _room?.FilterSuggestions(sender.Text);
        }
    }

    /// <summary>제안을 고르면 그 명령을 입력에 채운다(바로 보내지 않는다. 인자를 더 붙일 수 있게).</summary>
    private void OnRoomSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Daiso.Core.Prompts.SlashCommand command)
        {
            sender.Text = command.Invocation + " ";
        }
    }

    /// <summary>Enter(또는 돋보기)로 제출하면 콘솔로 보낸다. 제안을 고른 경우는 채우기만 하고 보내지 않는다.</summary>
    private void OnRoomQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_room is null || args.ChosenSuggestion is not null)
        {
            return;
        }

        _room.Input = sender.Text;
        _room.SendCommand.Execute(null);
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

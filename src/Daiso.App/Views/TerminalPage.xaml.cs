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
        }

        _room = room;
        room.Blocks.CollectionChanged += OnBlocksChanged;

        RoomCard.DataContext = room;
        RoomCard.Visibility = Visibility.Visible;
        RoomTabs.SelectedItem = room;

        try
        {
            await Embedded.InitializeAsync();
            Embedded.BindRoom(room);
            Embedded.FocusTerminal();
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

    /// <summary>Enter는 보내고, Shift+Enter는 줄바꿈. 터미널에 포커스가 있을 때는 xterm이 알아서 처리한다.</summary>
    private void OnRoomInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || _room is null)
        {
            return;
        }

        var shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (shift)
        {
            return;
        }

        e.Handled = true;
        _room.SendCommand.Execute(null);
    }

    private void ScrollChatToEnd() =>
        DispatcherQueue.TryEnqueue(() => ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, disableAnimation: true));

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            ViewModel.AppendPresetCommand.Execute(preset);
        }
    }
}

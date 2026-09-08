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
        ViewModel = App.Services.GetRequiredService<TerminalViewModel>();

        Loaded += async (_, _) =>
        {
            SyncTabFromViewModel();
            await ViewModel.RefreshInstalledAsync();
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

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string preset })
        {
            ViewModel.AppendPresetCommand.Execute(preset);
        }
    }
}

using Daiso.App.Controls;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Daiso.App.Views;

public sealed partial class SettingsPage : Page, IPageHeaderSource
{
    public SettingsPage()
    {
        InitializeComponent();
        FocusRelease.Attach(this);
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Header = new PageHeader("Settings_Title", ViewModel.RestartNotice);
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    /// <summary>세션 루트를 대화상자로 고른다.</summary>
    private async void OnPickSessionHomeClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            ViewModel.SessionHomeOverride = folder.Path;
        }
    }

    private void OnRemovePriceClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PriceRowViewModel row })
        {
            ViewModel.RemovePriceCommand.Execute(row);
        }
    }
}

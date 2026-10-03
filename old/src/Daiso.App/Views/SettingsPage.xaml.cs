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

    /// <summary>
    /// 플러그인 폴더를 연다. <b>없으면 만들어서 연다</b> — 처음 쓰는 사람에게 "그런 폴더 없음"을
    /// 보여 주고 끝내면 어디에 놓으라는 것인지 알 수 없다 (docs/PLUGIN_PLAN.md Stage 6).
    /// </summary>
    private async void OnOpenPluginFolderClick(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.PluginDirectory;

        try
        {
            Directory.CreateDirectory(path);
            await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.StatusText = ex.Message;
        }
    }
}

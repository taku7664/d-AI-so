using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        Shell.PropertyChanged += OnShellPropertyChanged;

        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    public DashboardViewModel ViewModel { get; }

    /// <summary>인덱싱 진행 상태. 끝나면 요약을 다시 읽는다.</summary>
    private ShellViewModel Shell { get; } = App.Services.GetRequiredService<ShellViewModel>();

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsIndexing) && !Shell.IsIndexing)
        {
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>세션 목록으로 보낸다.</summary>
    private void OnGoToSessionsClick(object sender, RoutedEventArgs e) => Go("Sessions");

    private static void Go(string tag) => (App.MainWindow as ShellWindow)?.NavigateTo(tag);

    /// <summary>최근 세션을 Terminal 화면에 채워 넣고 그 화면으로 보낸다.</summary>
    private void OnResumeRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentSessionViewModel row })
        {
            return;
        }

        ViewModel.PrepareResume(row.Session);
        Go("Terminal");
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolCardViewModel card })
        {
            await ViewModel.LoginCommand.ExecuteAsync(card);
        }
    }
}

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
        Usage = App.Services.GetRequiredService<UsageViewModel>();

        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            await Usage.LoadCommand.ExecuteAsync(null);
        };
    }

    public DashboardViewModel ViewModel { get; }

    /// <summary>사용량 탭.</summary>
    public UsageViewModel Usage { get; }

    /// <summary>세션 목록으로 보낸다.</summary>
    private void OnGoToSessionsClick(object sender, RoutedEventArgs e) => Go("Sessions");

    /// <summary>단가표를 넣을 수 있는 설정으로 보낸다.</summary>
    private void OnGoToPricesClick(object sender, RoutedEventArgs e) => Go("Settings");

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

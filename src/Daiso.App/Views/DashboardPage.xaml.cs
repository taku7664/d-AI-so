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

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolCardViewModel card })
        {
            await ViewModel.LoginCommand.ExecuteAsync(card);
        }
    }
}

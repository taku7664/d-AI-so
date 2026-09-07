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

        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    public DashboardViewModel ViewModel { get; }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolCardViewModel card })
        {
            await ViewModel.LoginCommand.ExecuteAsync(card);
        }
    }
}

using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
    }

    public SettingsViewModel ViewModel { get; }

    private void OnRemovePriceClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PriceRowViewModel row })
        {
            ViewModel.RemovePriceCommand.Execute(row);
        }
    }
}

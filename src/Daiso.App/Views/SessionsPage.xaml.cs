using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
    }

    public SessionsViewModel ViewModel { get; }
}

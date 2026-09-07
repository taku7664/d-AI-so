using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class TerminalPage : Page
{
    public TerminalPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TerminalViewModel>();
    }

    public TerminalViewModel ViewModel { get; }
}

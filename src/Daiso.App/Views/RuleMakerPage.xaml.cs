using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class RuleMakerPage : Page
{
    public RuleMakerPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<RuleMakerViewModel>();
    }

    public RuleMakerViewModel ViewModel { get; }
}

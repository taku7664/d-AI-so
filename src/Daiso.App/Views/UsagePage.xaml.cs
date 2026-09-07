using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

/// <summary>토큰 사용량. 일별·프로젝트별·모델별. (REQUIREMENTS §7)</summary>
public sealed partial class UsagePage : Page
{
    public UsagePage()
    {
        InitializeComponent();
        Usage = App.Services.GetRequiredService<UsageViewModel>();

        Loaded += async (_, _) => await Usage.LoadCommand.ExecuteAsync(null);
    }

    public UsageViewModel Usage { get; }

    /// <summary>단가를 넣을 수 있는 설정으로 보낸다.</summary>
    private void OnGoToPricesClick(object sender, RoutedEventArgs e) =>
        (App.MainWindow as ShellWindow)?.NavigateTo("Settings");
}

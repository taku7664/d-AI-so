using Daiso.App.Controls;
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
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);
        Usage = App.Services.GetRequiredService<UsageViewModel>();

        Loaded += async (_, _) => await Usage.LoadCommand.ExecuteAsync(null);
    }

    public UsageViewModel Usage { get; }

    /// <summary>일별 줄에 마우스가 올라오면 배경·막대를 밝히고 내역 글을 보인다.</summary>
    private void OnDayEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => HighlightDay(sender as Grid, true);

    private void OnDayExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => HighlightDay(sender as Grid, false);

    private static void HighlightDay(Grid? row, bool on)
    {
        if (row is null)
        {
            return;
        }

        row.Background = on
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var child in row.Children)
        {
            switch (child)
            {
                case Microsoft.UI.Xaml.Shapes.Rectangle bar:
                    bar.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "AccentFillColorSecondaryBrush" : "AccentFillColorDefaultBrush"];
                    break;
                case TextBlock text when Grid.GetColumn(text) == 3:
                    text.Opacity = on ? 1 : 0;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>탭을 누르면 뷰모델이 그 도구로 좁혀 다시 읽는다.</summary>
    private void OnToolTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);

        if (index >= 0 && index != Usage.SelectedTabIndex)
        {
            Usage.SelectedTabIndex = index;
        }
    }

    /// <summary>단가를 넣을 수 있는 설정으로 보낸다.</summary>
    private void OnGoToPricesClick(object sender, RoutedEventArgs e) =>
        (App.MainWindow as ShellWindow)?.NavigateTo("Settings");
}

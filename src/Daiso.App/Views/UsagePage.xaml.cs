using Daiso.App.Controls;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Daiso.App.Views;

/// <summary>토큰 사용량. 일별·프로젝트별·모델별. (REQUIREMENTS §7)</summary>
public sealed partial class UsagePage : Page, IPageHeaderSource
{
    public UsagePage()
    {
        InitializeComponent();
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);
        Usage = App.Services.GetRequiredService<UsageViewModel>();
        Header = new PageHeader("Usage_Title").Follow(Usage, nameof(UsageViewModel.StatusText), () => Usage.StatusText);

        // 뒤로/앞으로가 뷰모델의 탭을 바꾸면 탭 띠도 따라간다
        Usage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UsageViewModel.SelectedTabIndex))
            {
                SelectorBarVisuals.Select(ToolTabs, Usage.SelectedTabIndex);
            }
        };

        Loaded += async (_, _) =>
        {
            SelectorBarVisuals.Select(ToolTabs, Usage.SelectedTabIndex);
            await Usage.LoadCommand.ExecuteAsync(null);
        };
    }

    public UsageViewModel Usage { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    /// <summary>화면에 붙어 있는 일별 줄들. 포인터 밑의 줄을 여기서 찾는다.</summary>
    private readonly List<Grid> _dayRows = new();

    /// <summary>지금 밝혀 둔 줄.</summary>
    private Grid? _hoveredDay;

    private void OnDayRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row && !_dayRows.Contains(row))
        {
            _dayRows.Add(row);
        }
    }

    private void OnDayRowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid row)
        {
            return;
        }

        _dayRows.Remove(row);

        if (ReferenceEquals(_hoveredDay, row))
        {
            _hoveredDay = null;
        }
    }

    /// <summary>
    /// 어느 줄이 포인터 밑인지 카드 쪽에서 직접 정한다.
    /// 줄마다 PointerEntered/Exited 를 듣던 방식은 포인터가 줄 안의 자식(막대·글씨) 경계를 넘을 때마다
    /// Exited+Entered 가 잇달아 와서 초당 수십 번 깜빡였다. 좌표로 정하면 그 전환이 무의미해진다.
    /// </summary>
    private void OnDayAreaMoved(object sender, PointerRoutedEventArgs e)
    {
        Grid? under = null;

        foreach (var row in _dayRows)
        {
            var point = e.GetCurrentPoint(row).Position;

            if (point.X >= 0 && point.Y >= 0 && point.X <= row.ActualWidth && point.Y <= row.ActualHeight)
            {
                under = row;
                break;
            }
        }

        if (ReferenceEquals(_hoveredDay, under))
        {
            return;
        }

        HighlightDay(_hoveredDay, false);
        _hoveredDay = under;
        HighlightDay(under, true);
    }

    /// <summary>카드를 벗어나거나 포인터가 취소·캡처 해제되면 밝힘을 지운다.</summary>
    private void OnDayAreaLeft(object sender, PointerRoutedEventArgs e)
    {
        HighlightDay(_hoveredDay, false);
        _hoveredDay = null;
    }

    /// <summary>줄 하나의 배경을 켜고 끈다. 수치·내역은 툴팁이 맡는다.</summary>
    private static void HighlightDay(Grid? row, bool on)
    {
        if (row is null)
        {
            return;
        }

        // ListViewItem 의 호버 색. Subtle* 계열은 다크 테마에서 흰색 3% 수준이라 눈에 띄지 않는다
        row.Background = on
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ListViewItemBackgroundPointerOver"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
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

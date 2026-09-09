using Daiso.App.Strings;
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
        Header = new PageHeader("Usage_Title", UiStrings.Get("Usage_Subtitle"));

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

    /// <summary>
    /// 줄 하나를 켜고 끈다: 배경은 액센트 반투명, 막대는 밝은 액센트. 수치·내역은 툴팁이 맡는다.
    /// ListViewItemBackgroundPointerOver(Subtle 계열)는 다크에서 흰색 6% 라 눈에 띄지 않았고, Application.Current.Resources 로 꺼내면
    /// 창의 테마가 아니라 앱 기본 테마 값이 나와 더 흐려졌다. 그래서 줄의 ActualTheme 로 색을 직접 고른다.
    /// </summary>
    private static void HighlightDay(Grid? row, bool on)
    {
        if (row is null)
        {
            return;
        }

        var dark = row.ActualTheme == ElementTheme.Dark;
        var accent = (Windows.UI.Color)Application.Current.Resources[dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1"];

        row.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(on
            ? Windows.UI.Color.FromArgb(0x40, accent.R, accent.G, accent.B)
            : Microsoft.UI.Colors.Transparent);

        // 막대의 원래 브러시는 XAML 의 ThemeResource(창 테마를 따른다)다. 끌 때 리소스에서 다시 꺼내면 다른 테마 값이 나와
        // 막대가 진해지므로, 켤 때 원래 것을 Tag 에 두고 끌 때 그대로 되돌린다
        if (row.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().FirstOrDefault() is { } bar)
        {
            if (on)
            {
                bar.Tag ??= bar.Fill;
                bar.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(accent);
            }
            else if (bar.Tag is Microsoft.UI.Xaml.Media.Brush original)
            {
                bar.Fill = original;
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

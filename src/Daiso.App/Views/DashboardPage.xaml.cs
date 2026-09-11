using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

/// <summary>
/// 요약 화면. 도구별 설치·로그인 상태와 최근 세션·사용량. (REQUIREMENTS §3)
///
/// <para>
/// 여기에는 <b>화면 연출만</b> 있다. 눌렀을 때 벌어지는 일은 전부 뷰모델의 명령이다 —
/// 예전에는 대화상자를 띄우려면 페이지의 <c>XamlRoot</c> 가 필요해서 프로필 저장·전환·삭제가
/// 이 파일의 <c>Click</c> 핸들러였고, 그 탓에 도구 카드의 생김새가 페이지에 묶여 있었다.
/// <see cref="IDialogHost"/> · <see cref="INavigator"/> 가 그 매듭을 풀었다 (docs/REVIEW_BACKLOG.md D1).
/// </para>
/// </summary>
public sealed partial class DashboardPage : Page, IPageHeaderSource
{
    public DashboardPage()
    {
        InitializeComponent();
        // 도구 탭은 XAML 이 아니라 지금 앱이 아는 도구 목록이 채운다 (docs/PLUGIN_PLAN.md Stage 3)
        SelectorBarVisuals.FillToolTabs(ToolTabs);
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        Header = new PageHeader("Dashboard_Title", UiStrings.Get("Dashboard_ToolStatusHint"));
        Shell.PropertyChanged += OnShellPropertyChanged;

        // 뒤로/앞으로가 뷰모델의 탭을 바꾸면 탭 띠도 따라간다
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.SelectedTabIndex))
            {
                SelectorBarVisuals.Select(ToolTabs, ViewModel.SelectedTabIndex);
            }
        };

        Loaded += async (_, _) =>
        {
            SelectorBarVisuals.Select(ToolTabs, ViewModel.SelectedTabIndex);
            await UiCommands.RunAsync(ViewModel.LoadCommand);
            ViewModel.SyncProfiles();
        };
    }

    public DashboardViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    /// <summary>인덱싱 진행 상태. 끝나면 요약을 다시 읽는다.</summary>
    private ShellViewModel Shell { get; } = App.Services.GetRequiredService<ShellViewModel>();

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsIndexing) && !Shell.IsIndexing)
        {
            UiCommands.Start(ViewModel.LoadCommand);
        }
    }

    // ── 도구 카드 띠 ───────────────────────────────────────────────────────
    //
    // 카드는 언제나 한 줄이고 넘치면 가로로 스크롤한다. 폭은 "두 장이 딱 들어가는 값" —
    // 지금 보이는 칸의 절반이라 창 크기에 따라 달라지고, 그래서 XAML 이 정하지 못한다.
    //
    // StackLayout 은 항목에게 원하는 폭을 그대로 주므로 카드가 제 내용대로 커진다.
    // 여기서 폭을 못 박아야 카드 셋이 같은 크기로 보인다.

    /// <summary>한 화면에 보일 카드 수. 셋으로 늘리려면 이 값만 바꾼다.</summary>
    private const int VisibleCards = 2;

    /// <summary>카드가 이보다 좁아지면 안에 든 글이 잘린다. 그때는 두 장이 안 들어가고 스크롤로 넘긴다.</summary>
    private const double MinimumCardWidth = 300;

    private double _cardWidth;

    private void OnToolStripSizeChanged(object sender, SizeChangedEventArgs e) => ApplyCardWidth();

    /// <summary>카드가 화면에 올라올 때마다 지금 폭을 입힌다. 가상화라 스크롤 중에도 새로 올라온다.</summary>
    private void OnToolCardPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (_cardWidth > 0 && args.Element is FrameworkElement card)
        {
            card.Width = _cardWidth;
        }
    }

    /// <summary>보이는 칸을 재어 카드 폭을 정하고, 이미 올라와 있는 카드에도 입힌다.</summary>
    private void ApplyCardWidth()
    {
        var spacing = (double)Application.Current.Resources["GapXLarge"];
        var available = ToolStrip.ViewportWidth > 0 ? ToolStrip.ViewportWidth : ToolStrip.ActualWidth;

        if (available <= 0)
        {
            return;
        }

        var width = Math.Max(
            MinimumCardWidth,
            (available - (spacing * (VisibleCards - 1))) / VisibleCards);

        if (Math.Abs(width - _cardWidth) < 0.5)
        {
            return;
        }

        _cardWidth = width;

        for (var i = 0; i < ViewModel.VisibleTools.Count; i++)
        {
            if (ToolCards.TryGetElement(i) is FrameworkElement card)
            {
                card.Width = width;
            }
        }
    }

    /// <summary>탭을 누르면 뷰모델이 카드·최근 세션·통계를 다시 거른다.</summary>
    private void OnToolTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);

        if (index >= 0 && index != ViewModel.SelectedTabIndex)
        {
            ViewModel.SelectedTabIndex = index;
        }
    }
}

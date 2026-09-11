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

        // 탭으로 거르면 카드 수가 바뀌어 넘치는 양도 바뀐다. 화살표가 그대로 남으면 못 누르는 단추가 된다
        ViewModel.VisibleTools.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncArrows);

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

    private void OnToolStripSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyCardWidth();
        SyncArrows();
    }

    private void OnToolStripViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => SyncArrows();

    private void OnToolCardsPrevClick(object sender, RoutedEventArgs e) => ScrollBy(-1);

    private void OnToolCardsNextClick(object sender, RoutedEventArgs e) => ScrollBy(1);

    /// <summary>카드 한 장 + 간격만큼 민다. 반 장씩 걸치면 어디까지 봤는지 알 수 없다.</summary>
    private void ScrollBy(int cards)
    {
        var step = (_cardWidth > 0 ? _cardWidth : MinimumCardWidth) + Spacing;

        ToolStrip.ChangeView(ToolStrip.HorizontalOffset + (step * cards), null, null, disableAnimation: false);
    }

    /// <summary>
    /// 갈 수 있는 쪽의 화살표만 보인다. 넘칠 것이 없으면 둘 다 감춘다 —
    /// 누를 수 없는 단추가 떠 있으면 그것도 거짓말이다.
    /// </summary>
    private void SyncArrows()
    {
        // 1px 은 반올림 오차다. 이걸 안 두면 끝까지 밀어도 화살표가 남는다
        const double Slack = 1;

        var scrollable = ToolStrip.ScrollableWidth;

        ToolPrevButton.Visibility = scrollable > Slack && ToolStrip.HorizontalOffset > Slack
            ? Visibility.Visible
            : Visibility.Collapsed;

        ToolNextButton.Visibility = scrollable > Slack && ToolStrip.HorizontalOffset < scrollable - Slack
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static double Spacing => (double)Application.Current.Resources["GapXLarge"];

    /// <summary>
    /// 카드가 화면에 올라올 때마다 지금 폭을 입히고, 가운데로 굴릴 계기 둘을 붙인다.
    /// 가상화라 스크롤 중에도 새로 올라온다.
    /// <para>
    /// <b>둘 다 필요하다.</b> <c>GotFocus</c> 는 포커스가 <b>들어올 때만</b> 뜬다 —
    /// 이미 고른 카드를 다시 눌러도 아무 일이 없었다 (2026-09-11 사람의 지적).
    /// <c>Click</c> 은 누를 때마다 뜨니 다시 눌러도 가운데로 간다.
    /// 반대로 <c>Click</c> 만 두면 Tab 으로 옮겨 온 카드가 화면 밖에 남는다.
    /// </para>
    /// </summary>
    private void OnToolCardPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement card)
        {
            return;
        }

        if (_cardWidth > 0)
        {
            card.Width = _cardWidth;
        }

        // 같은 요소가 재사용되므로 두 번 붙지 않게 먼저 뗀다
        card.GotFocus -= OnToolCardCentreRequested;
        card.GotFocus += OnToolCardCentreRequested;

        if (card is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
        {
            button.Click -= OnToolCardCentreRequested;
            button.Click += OnToolCardCentreRequested;
        }
    }

    /// <summary>포커스가 왔거나 눌렸다. 둘 다 "이 카드를 보고 싶다"는 뜻이라 같은 곳으로 보낸다.</summary>
    private void OnToolCardCentreRequested(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            CenterCard(card);
        }
    }

    /// <summary>
    /// 그 카드를 띠 가운데로 굴린다. <b>끝쪽 카드는 갈 수 있는 만큼만</b> 간다 —
    /// 가운데로 보내려면 빈 자리를 만들어야 하는데, 그러면 카드 옆에 빈칸이 생겨 더 어색하다.
    /// </summary>
    private void CenterCard(FrameworkElement card)
    {
        var index = ToolCards.GetElementIndex(card);
        var step = (_cardWidth > 0 ? _cardWidth : MinimumCardWidth) + Spacing;

        if (index < 0 || ToolStrip.ViewportWidth <= 0)
        {
            return;
        }

        // 카드 왼쪽 끝을 가운데로 보낸 뒤 카드 절반만큼 되돌리면 카드 가운데가 화면 가운데에 온다
        var centered = (index * step) + (_cardWidth / 2) - (ToolStrip.ViewportWidth / 2);
        var target = Math.Clamp(centered, 0, ToolStrip.ScrollableWidth);

        ToolStrip.ChangeView(target, null, null, disableAnimation: false);
    }

    /// <summary>보이는 칸을 재어 카드 폭을 정하고, 이미 올라와 있는 카드에도 입힌다.</summary>
    private void ApplyCardWidth()
    {
        var available = ToolStrip.ViewportWidth > 0 ? ToolStrip.ViewportWidth : ToolStrip.ActualWidth;

        if (available <= 0)
        {
            return;
        }

        var width = Math.Max(
            MinimumCardWidth,
            (available - (Spacing * (VisibleCards - 1))) / VisibleCards);

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

        // 폭이 바뀌면 넘치는 양도 바뀐다. 레이아웃이 끝난 뒤에 재야 맞다
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncArrows);
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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Controls;

/// <summary>본문 폭. 두 값뿐이다. 세 번째는 없다 (UI_REFACTOR_PLAN §8.2).</summary>
public enum PageLayout
{
    /// <summary>읽는 화면. <c>PageMaxWidth</c>(1280)까지 자라고 그 위로는 왼쪽 정렬 + 오른쪽 여백. 본문이 한 덩어리로 스크롤한다.</summary>
    Reading,

    /// <summary>넓은 화면. 창을 다 쓴다. 스크롤은 칸마다 페이지가 따로 한다.</summary>
    Wide,
}

/// <summary>
/// 페이지 본문의 골격. 일곱 페이지의 루트다 (UI_REFACTOR_PLAN §6).
/// 대제목은 셸이 그리므로(<see cref="PageHeader"/>) 여기에는 없다. 슬롯은 위에서부터
/// <see cref="Commands"/>(명령 줄) → <see cref="Filters"/>(필터·탭 줄) → <see cref="Body"/>(본문) → <see cref="Footer"/>(푸터).
/// 비운 슬롯은 줄 자체가 사라진다. 여백은 <c>PagePadding</c>(24), 줄 사이는 12 한 벌만 쓴다.
/// 머리와 푸터는 스크롤하지 않는다. <see cref="PageLayout.Reading"/>이면 본문을 이 컨트롤이 스크롤에 담고,
/// <see cref="PageLayout.Wide"/>면 본문이 칸마다 스스로 스크롤한다.
/// <c>Grid</c>를 상속하는 이유: <see cref="FocusRelease"/>가 페이지 루트 패널에 포커스 싱크를 넣기 때문이다.
/// </summary>
public sealed class PageBody : Grid
{
    /// <summary>골격 줄 사이 간격. 카드 사이(16)와 다르다 — 그건 본문 안의 일이다.</summary>
    private const double RowGap = 12;

    private readonly StackPanel _head;
    private readonly ScrollViewer _scroll;
    private readonly Grid _scrollInner;
    private readonly ColumnDefinition _column;
    private readonly ColumnDefinition _scrollColumn;

    public PageBody()
    {
        Padding = (Thickness)Application.Current.Resources["PagePadding"];

        // 폭은 열이 정한다: 최대 폭까지 채우고 남는 자리는 오른쪽 빈 열로. 내용이 적어도 줄이 움직이지 않는다
        _column = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        ColumnDefinitions.Add(_column);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _head = new StackPanel { Spacing = RowGap, Visibility = Visibility.Collapsed };
        SetRow(_head, 0);
        SetColumn(_head, 0);
        Children.Add(_head);

        // Reading 의 스크롤. 좌우 여백을 안쪽으로 밀어 넣어 스크롤바가 창 오른쪽 끝에 붙는다 (PowerToys 와 같다)
        _scrollColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _scrollInner = new Grid();
        _scrollInner.ColumnDefinitions.Add(_scrollColumn);
        _scrollInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _scrollInner,
            Visibility = Visibility.Collapsed,
        };
        SetRow(_scroll, 1);
        SetColumn(_scroll, 0);
        SetColumnSpan(_scroll, 2);
        Children.Add(_scroll);

        ApplyLayout();
    }

    // ── 슬롯 ─────────────────────────────────────────────────────────────

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(PageLayout), typeof(PageBody),
        new PropertyMetadata(PageLayout.Reading, (d, _) => ((PageBody)d).ApplyLayout()));

    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(
        nameof(Commands), typeof(FrameworkElement), typeof(PageBody),
        new PropertyMetadata(null, (d, e) => ((PageBody)d).ReplaceHead(e.OldValue as FrameworkElement, e.NewValue as FrameworkElement, index: 0)));

    public static readonly DependencyProperty FiltersProperty = DependencyProperty.Register(
        nameof(Filters), typeof(FrameworkElement), typeof(PageBody),
        new PropertyMetadata(null, (d, e) => ((PageBody)d).ReplaceHead(e.OldValue as FrameworkElement, e.NewValue as FrameworkElement, index: 1)));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(FrameworkElement), typeof(PageBody),
        new PropertyMetadata(null, (d, e) => ((PageBody)d).ReplaceBody(e.OldValue as FrameworkElement, e.NewValue as FrameworkElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(FrameworkElement), typeof(PageBody),
        new PropertyMetadata(null, (d, e) => ((PageBody)d).ReplaceFooter(e.OldValue as FrameworkElement, e.NewValue as FrameworkElement)));

    /// <summary><c>Reading</c> / <c>Wide</c>. 페이지가 폭을 직접 정하지 않는다.</summary>
    public PageLayout Layout
    {
        get => (PageLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>명령 줄. 다시 읽기·새로 만들기·저장 같은 행동. 없으면 줄이 사라진다.</summary>
    public FrameworkElement? Commands
    {
        get => (FrameworkElement?)GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }

    /// <summary>필터·탭 줄. 도구 탭·기간 콤보·검색. 없으면 줄이 사라진다.</summary>
    public FrameworkElement? Filters
    {
        get => (FrameworkElement?)GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    /// <summary>본문. 남는 높이를 다 쓴다.</summary>
    public FrameworkElement? Body
    {
        get => (FrameworkElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>푸터. 저장 바·안내 줄. 저장은 언제나 여기다 (§8.3). 없으면 줄이 사라진다.</summary>
    public FrameworkElement? Footer
    {
        get => (FrameworkElement?)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    // ── 조립 ─────────────────────────────────────────────────────────────

    private void ReplaceHead(FrameworkElement? old, FrameworkElement? fresh, int index)
    {
        if (old is not null)
        {
            _head.Children.Remove(old);
        }

        if (fresh is not null)
        {
            // 명령 줄이 필터 줄보다 위. 둘 중 하나만 있어도 순서가 지켜지게 자리로 넣는다
            var at = index == 0 || _head.Children.Count == 0 ? 0 : _head.Children.Count;
            _head.Children.Insert(Math.Min(at, _head.Children.Count), fresh);
        }

        _head.Visibility = _head.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyGaps();
    }

    private void ReplaceBody(FrameworkElement? old, FrameworkElement? fresh)
    {
        if (old is not null)
        {
            _scrollInner.Children.Remove(old);
            Children.Remove(old);
        }

        if (fresh is not null)
        {
            SetRow(fresh, 1);
            SetColumn(fresh, 0);
        }

        ApplyLayout();
    }

    private void ReplaceFooter(FrameworkElement? old, FrameworkElement? fresh)
    {
        if (old is not null)
        {
            Children.Remove(old);
        }

        if (fresh is not null)
        {
            SetRow(fresh, 2);
            SetColumn(fresh, 0);
            Children.Add(fresh);
        }

        ApplyGaps();
    }

    /// <summary>Reading 은 본문을 스크롤에 담고 폭을 1280 으로 막는다. Wide 는 본문을 그대로 두고 창을 다 쓴다.</summary>
    private void ApplyLayout()
    {
        var body = Body;
        var reading = Layout == PageLayout.Reading;
        var max = reading ? (double)Application.Current.Resources["PageMaxWidth"] : double.PositiveInfinity;

        _column.MaxWidth = max;
        _scrollColumn.MaxWidth = max;

        if (body is not null)
        {
            _scrollInner.Children.Remove(body);
            Children.Remove(body);

            if (reading)
            {
                _scrollInner.Children.Add(body);
            }
            else
            {
                Children.Add(body);
            }
        }

        _scroll.Visibility = reading && body is not null ? Visibility.Visible : Visibility.Collapsed;
        ApplyGaps();
    }

    /// <summary>
    /// 줄 사이 12. Grid.RowSpacing 은 빈 줄에도 간격을 넣으므로 있는 줄에만 여백을 준다.
    /// Reading 의 스크롤은 좌우 여백을 안으로 접어 스크롤바가 창 끝에 붙고, 푸터가 없으면 아래 여백도 스크롤 안으로 접는다.
    /// </summary>
    private void ApplyGaps()
    {
        var pad = Padding;
        var hasHead = _head.Visibility == Visibility.Visible;
        var hasFooter = Footer is not null;
        var top = hasHead ? RowGap : 0;

        if (Layout == PageLayout.Reading)
        {
            var bottom = hasFooter ? 0 : pad.Bottom;
            _scroll.Margin = new Thickness(-pad.Left, top, -pad.Right, -bottom);
            _scroll.Padding = new Thickness(pad.Left, 0, pad.Right, bottom);
        }
        else if (Body is { } body)
        {
            body.Margin = new Thickness(0, top, 0, 0);
        }

        if (Footer is { } footer)
        {
            footer.Margin = new Thickness(0, RowGap, 0, 0);
        }
    }
}

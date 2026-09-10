using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Daiso.App.Controls;

/// <summary>
/// 고르는 칸. 누르거나 포커스가 오면 <b>맨 위에 검색칸이 붙은 목록</b>이 열린다.
/// 목록은 <see cref="VisibleItemCount"/>(기본 5)줄까지만 보이고 그보다 많으면 안에서 스크롤한다.
/// <code>
/// &lt;controls:SearchablePicker
///     ItemsSource="{x:Bind ViewModel.Folders}"
///     SelectedItem="{x:Bind ViewModel.Folder, Mode=TwoWay}"
///     PlaceholderText="폴더를 선택해주세요"
///     AllowFreeText="True" /&gt;
/// </code>
/// <para>
/// <b>왜 <c>ComboBox</c> 를 안 쓰는가.</b> WinUI 의 콤보 팝업에는 검색칸을 넣을 자리가 없고,
/// 편집형 콤보는 겉모습이 그냥 입력 칸이라 "고를 것이 있다"는 사실이 화살표 하나에 걸린다.
/// 목록이 서른 줄이 되면 훑어 고르는 것이 불가능해진다 (2026-09-11 사람의 요청).
/// 화면마다 따로 만들지 않고 이 컨트롤 하나로 모은다.
/// </para>
/// <para>
/// <b>겪은 것 셋.</b>
/// <list type="number">
/// <item>팝오버는 무한 폭으로 재어진다 — 열 때 칸 폭을 직접 넣어 줘야 목록이 칸 밖으로 삐져나가지 않는다.</item>
/// <item>목록 높이를 내용에 맡기면 창 높이만큼 늘어난다. <c>MaxHeight</c> 를 줄 수 × 줄 높이로 못 박는다.</item>
/// <item>포커스로 여는 것과 눌러서 여는 것이 겹치면 열자마자 닫힌다. 연 직후 잠깐은 다시 열지 않는다.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SearchablePicker : Grid
{
    /// <summary>목록 한 줄의 높이. 세 2단 화면의 목록 줄과 같은 값이다.</summary>
    private const double RowHeight = 40;

    /// <summary>닫은 직후 이 시간 안에는 다시 열지 않는다. 고르고 닫자마자 포커스가 돌아와 다시 열리는 것을 막는다.</summary>
    private const long ReopenGuardMilliseconds = 300;

    private readonly Button _face = new();
    private readonly ContentPresenter _facePresenter = new();
    private readonly TextBlock _placeholder = new();
    private readonly TextBox _search = new();
    private readonly ListView _list = new();
    private readonly Grid _popupRoot = new();
    private readonly Flyout _flyout = new();
    private readonly ObservableCollection<object> _shown = [];

    private long _closedAt;
    private bool _syncing;

    public SearchablePicker()
    {
        _placeholder.Style = (Style)Application.Current.Resources["MutedText"];
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        _placeholder.TextWrapping = TextWrapping.NoWrap;

        _facePresenter.VerticalAlignment = VerticalAlignment.Center;
        _facePresenter.HorizontalAlignment = HorizontalAlignment.Left;

        var chevron = new FontIcon
        {
            Glyph = "",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var faceGrid = new Grid();
        faceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        faceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // 고른 것이 있으면 발표자를, 없으면 자리표시자를 보인다. 둘 다 같은 칸에 겹쳐 둔다
        var left = new Grid();
        left.Children.Add(_placeholder);
        left.Children.Add(_facePresenter);
        SetColumn(left, 0);
        SetColumn(chevron, 1);
        faceGrid.Children.Add(left);
        faceGrid.Children.Add(chevron);

        _face.Content = faceGrid;
        _face.HorizontalAlignment = HorizontalAlignment.Stretch;
        _face.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _face.Padding = new Thickness(11, 6, 11, 6);
        _face.Click += (_, _) => Open();
        _face.GotFocus += (_, _) => Open();

        _search.PlaceholderText = SearchPlaceholder;
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.KeyDown += OnSearchKeyDown;

        _list.ItemsSource = _shown;
        // 높이를 여기서 미리 못 박는다. 열릴 때 처음 재면 이미 늦다 — 아래 설명(Opening) 참고
        _list.MaxHeight = VisibleItemCount * RowHeight;
        _list.SelectionMode = ListViewSelectionMode.Single;
        _list.SelectionChanged += OnListSelectionChanged;
        _list.ItemContainerStyle = BuildRowStyle();

        _popupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _popupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _popupRoot.RowSpacing = 6;
        SetRow(_search, 0);
        SetRow(_list, 1);
        _popupRoot.Children.Add(_search);
        _popupRoot.Children.Add(_list);

        _flyout.Content = _popupRoot;
        _flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        _flyout.FlyoutPresenterStyle = BuildPresenterStyle();
        // 크기는 Opening 에서 정한다. Opened 는 자리를 이미 잡은 뒤라 늦다 (아래 OnFlyoutOpening 참고)
        _flyout.Opening += OnFlyoutOpening;
        _flyout.Opened += OnFlyoutOpened;
        _flyout.Closed += (_, _) => _closedAt = Environment.TickCount64;

        FlyoutBase.SetAttachedFlyout(_face, _flyout);
        Children.Add(_face);
    }

    // ── 붙일 수 있는 것들 ─────────────────────────────────────────────────

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(SearchablePicker), new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>고를 것들. 목록이 바뀌면 검색 결과도 다시 만든다.</summary>
    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchablePicker), new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>고른 것. 아무것도 안 골랐으면 null 이고 자리표시자가 보인다.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(SearchablePicker), new PropertyMetadata(null, OnItemTemplateChanged));

    /// <summary>목록 한 줄과 고른 것을 그리는 틀. 없으면 <c>ToString()</c> 을 쓴다.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(SearchablePicker), new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    /// <summary>아직 안 골랐을 때 칸에 보이는 글.</summary>
    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly DependencyProperty SearchPlaceholderProperty =
        DependencyProperty.Register(nameof(SearchPlaceholder), typeof(string), typeof(SearchablePicker), new PropertyMetadata(string.Empty, OnSearchPlaceholderChanged));

    /// <summary>목록 맨 위 검색칸의 자리표시자.</summary>
    public string SearchPlaceholder
    {
        get => (string)GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    public static readonly DependencyProperty VisibleItemCountProperty =
        DependencyProperty.Register(nameof(VisibleItemCount), typeof(int), typeof(SearchablePicker), new PropertyMetadata(5, OnVisibleItemCountChanged));

    /// <summary>한 번에 보일 줄 수. 그보다 많으면 목록 안에서 스크롤한다.</summary>
    public int VisibleItemCount
    {
        get => (int)GetValue(VisibleItemCountProperty);
        set => SetValue(VisibleItemCountProperty, value);
    }

    public static readonly DependencyProperty IsPickerEnabledProperty =
        DependencyProperty.Register(nameof(IsPickerEnabled), typeof(bool), typeof(SearchablePicker), new PropertyMetadata(true, OnIsPickerEnabledChanged));

    /// <summary>
    /// 지금 고를 수 있는가. 거짓이면 칸이 잠긴 모양이 되고 목록도 열리지 않는다.
    /// <para>
    /// <c>IsEnabled</c> 가 아니라 따로 둔 이름이다 — 이 컨트롤은 <see cref="Grid"/> 라
    /// <c>Control.IsEnabled</c> 를 물려받지 않는다. 같은 이름을 새로 만들면 XAML 에서 둘 중 어느 것이
    /// 걸린 것인지 알 수 없어진다.
    /// </para>
    /// </summary>
    public bool IsPickerEnabled
    {
        get => (bool)GetValue(IsPickerEnabledProperty);
        set => SetValue(IsPickerEnabledProperty, value);
    }

    public static readonly DependencyProperty AllowFreeTextProperty =
        DependencyProperty.Register(nameof(AllowFreeText), typeof(bool), typeof(SearchablePicker), new PropertyMetadata(false));

    /// <summary>
    /// 목록에 없는 것도 적어서 쓸 수 있는가. 참이면 검색칸에서 Enter 를 누를 때
    /// 맞는 줄이 없으면 <see cref="FreeTextSubmitted"/> 로 적은 글을 그대로 넘긴다(폴더 경로처럼).
    /// </summary>
    public bool AllowFreeText
    {
        get => (bool)GetValue(AllowFreeTextProperty);
        set => SetValue(AllowFreeTextProperty, value);
    }

    /// <summary>목록에 없는 글을 그대로 쓰겠다고 했다. <see cref="AllowFreeText"/> 일 때만 온다.</summary>
    public event EventHandler<string>? FreeTextSubmitted;

    /// <summary>
    /// 사람이 목록에서 골랐다. <see cref="SelectedItem"/> 을 그냥 묶어도 되지만,
    /// "값이 바뀐 것"과 "사람이 고른 것"을 갈라야 하는 화면이 있어 따로 알린다(폴더 단계처럼).
    /// </summary>
    public event EventHandler<object>? SelectionChanged;

    /// <summary>이 칸에 포커스를 준다. 단계가 열릴 때 화면이 답할 칸으로 포커스를 옮기는 데 쓴다.</summary>
    public void FocusPicker() => _face.Focus(FocusState.Programmatic);

    // ── 열고 닫기 ─────────────────────────────────────────────────────────

    /// <summary>목록을 편다. 검색칸이 비워지고 거기로 포커스가 간다.</summary>
    public void Open()
    {
        if (!IsPickerEnabled || _flyout.IsOpen || Environment.TickCount64 - _closedAt < ReopenGuardMilliseconds)
        {
            return;
        }

        FlyoutBase.ShowAttachedFlyout(_face);
    }

    /// <summary>
    /// 열기 <b>직전</b>에 크기를 정한다. <c>Opened</c> 에서 정하면 늦다 —
    /// WinUI 는 그 전에 팝오버를 재어 <b>아래에 놓을지 위에 놓을지</b>를 이미 결정한다.
    /// 높이를 안 정해 준 채로 재면 서른일곱 줄짜리 목록이 통째로 재어져 "아래에 안 들어간다"가 되고,
    /// 팝오버가 칸 위로 뒤집혀 열렸다 (2026-09-11 사람의 지적. 짧은 목록만 아래로 열려서 더 헷갈렸다).
    /// </summary>
    private void OnFlyoutOpening(object? sender, object e)
    {
        // 팝오버는 무한 폭으로 재어진다. 칸 폭을 직접 넣어야 목록이 칸에 맞는다
        _popupRoot.Width = Math.Max(ActualWidth - 24, 200);
        _list.MaxHeight = Math.Max(VisibleItemCount, 1) * RowHeight;

        _search.Text = string.Empty;
        ApplyFilter();
    }

    private void OnFlyoutOpened(object? sender, object e) =>
        _search.DispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _search.Focus(FocusState.Programmatic));

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down when _shown.Count > 0:
                // 검색칸에서 아래로 내려가면 첫 줄로 간다. 손이 마우스로 옮겨 가지 않게 한다
                _list.SelectedIndex = 0;
                (_list.ContainerFromIndex(0) as Control)?.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;

            case VirtualKey.Enter when _shown.Count > 0:
                SelectedItem = _shown[0];
                _flyout.Hide();
                e.Handled = true;
                break;

            case VirtualKey.Enter when AllowFreeText && _search.Text.Trim().Length > 0:
                var typed = _search.Text.Trim();
                _flyout.Hide();
                FreeTextSubmitted?.Invoke(this, typed);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _list.SelectedItem is null)
        {
            return;
        }

        var picked = _list.SelectedItem;
        SelectedItem = picked;
        _flyout.Hide();
        SelectionChanged?.Invoke(this, picked);
    }

    // ── 목록 ──────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var query = _search.Text.Trim();

        _syncing = true;

        try
        {
            _shown.Clear();

            if (ItemsSource is IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is not null && Matches(item, query))
                    {
                        _shown.Add(item);
                    }
                }
            }

            _list.SelectedItem = SelectedItem is { } selected && _shown.Contains(selected) ? selected : null;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>글자가 들어 있으면 남긴다. 무엇으로 비교할지는 항목의 <c>ToString()</c> 이 정한다.</summary>
    private static bool Matches(object item, string query) =>
        query.Length == 0
        || (item.ToString() ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SyncFace()
    {
        _facePresenter.Content = SelectedItem;
        _facePresenter.ContentTemplate = ItemTemplate;
        _facePresenter.Visibility = SelectedItem is null ? Visibility.Collapsed : Visibility.Visible;
        _placeholder.Visibility = SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>목록 줄 높이를 못 박는다. 줄마다 높이가 다르면 "다섯 줄"이라는 약속이 깨진다.</summary>
    private static Style BuildRowStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(HeightProperty, RowHeight));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    private static Style BuildPresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(8)));
        style.Setters.Add(new Setter(MaxWidthProperty, double.PositiveInfinity));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        return style;
    }

    // ── 붙임 속성이 바뀌었을 때 ───────────────────────────────────────────

    private static void OnItemsSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not SearchablePicker picker)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= picker.OnSourceCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged fresh)
        {
            fresh.CollectionChanged += picker.OnSourceCollectionChanged;
        }

        picker.ApplyFilter();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyFilter();

    private static void OnSelectedItemChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker.SyncFace();
        }
    }

    private static void OnItemTemplateChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker._list.ItemTemplate = picker.ItemTemplate;
            picker.SyncFace();
        }
    }

    private static void OnPlaceholderChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker._placeholder.Text = picker.PlaceholderText;
        }
    }

    private static void OnSearchPlaceholderChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker._search.PlaceholderText = picker.SearchPlaceholder;
        }
    }

    private static void OnIsPickerEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker._face.IsEnabled = picker.IsPickerEnabled;
        }
    }

    private static void OnVisibleItemCountChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is SearchablePicker picker)
        {
            picker._list.MaxHeight = Math.Max(picker.VisibleItemCount, 1) * RowHeight;
        }
    }
}

using Daiso.App.Services;
using Daiso.App.ViewModels;
using Daiso.App.Views;
using Daiso.App.Strings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Daiso.App;

/// <summary>다섯 페이지를 담는 창. 상태바에 인덱싱 진행률을 보여준다. (ARCHITECTURE §6)</summary>
public sealed partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly ISettingsStore _settings;
    private readonly NavigationHistory _history = new();

    /// <summary>OLE 파일 드롭. XAML AllowDrop 이 이 환경에서 등록되지 않아 직접 등록한다 (FileDropTarget 주석).</summary>
    private readonly FileDropTarget _fileDrop;

    /// <summary>지금 보고 있는 페이지의 Tag. 자리를 적을 때 쓴다.</summary>
    private string _pageTag = "Dashboard";

    public ShellWindow(ShellViewModel viewModel, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(settings);

        _viewModel = viewModel;
        _settings = settings;

        InitializeComponent();

        _viewModel.Dispatch = action => RootGrid.DispatcherQueue.TryEnqueue(() => action());
        _viewModel.PropertyChanged += (_, _) => ApplyStatus();
        ApplyStatus();

        ExtendTitleBar();
        ApplyTheme();
        WatchThemeSetting();
        RestoreWindowSize();
        ApplyIcon();

        _fileDrop = new FileDropTarget(() => ContentFrame.Content as IFileDropSink);
        Activated += (_, _) => RegisterFileDrop();

        Navigate("Dashboard");
        WatchSpots();
        // 뷰모델이 대화상자를 띄우고 화면을 옮길 수 있도록 창의 것을 건넨다 (docs/REVIEW_BACKLOG.md D1).
        // XamlRoot 는 값이 아니라 찾는 함수로 준다 — 이 생성자 시점에는 아직 null 이다
        App.Services.GetRequiredService<DialogHost>().Attach(() => RootGrid.XamlRoot);
        App.Services.GetRequiredService<Navigator>().Attach(NavigateTo);

        _viewModel.StartBackgroundRefresh();

        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>다른 페이지의 안내에서 이 페이지로 보내 달라고 할 때 쓴다. 왼쪽 선택 표시도 맞춘다.</summary>
    /// <summary>열린 방 등록부. 좌측 메뉴 터미널 항목의 안 본 답 점이 이걸 본다.</summary>
    public ViewModels.RoomManager Rooms => App.Rooms;

    public void NavigateTo(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                Navigation.SelectedItem = item;
                return;
            }
        }

        Navigate(tag);
    }

    /// <summary>Ctrl+1~7로 왼쪽 메뉴 순서대로 이동한다.</summary>
    private void OnNavAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var index = sender.Key - Windows.System.VirtualKey.Number1;
        var items = Navigation.MenuItems.OfType<NavigationViewItem>().ToList();

        if (index >= 0 && index < items.Count)
        {
            Navigation.SelectedItem = items[index];
            args.Handled = true;
        }
    }

    /// <summary>새 페이지의 제목·부제를 Header 에 싣는다. 페이지가 IPageHeaderSource 가 아니면 제목이 비는 것이 보인다.</summary>
    private void OnFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        Navigation.Header = (e.Content as Controls.IPageHeaderSource)?.Header;

        // 자식 창(XAML 아일랜드·WebView2)은 나중에 생기므로 페이지를 옮길 때마다 아직 안 된 창을 등록한다
        RegisterFileDrop();
    }

    private void RegisterFileDrop()
    {
        if (_fileDrop is not null)
        {
            _fileDrop.RegisterAll(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var page = tag switch
        {
            "Usage" => typeof(UsagePage),
            "Terminal" => typeof(TerminalPage),
            "Sessions" => typeof(SessionsPage),
            "RuleMaker" => typeof(RuleMakerPage),
            "Prompts" => typeof(PromptsPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }

        _pageTag = tag;
        RecordSpot();
    }

    // ── 뒤로/앞으로 (마우스 엄지 버튼) ────────────────────────────────────

    private DashboardViewModel Dashboard { get; } = App.Services.GetRequiredService<DashboardViewModel>();

    private UsageViewModel Usage { get; } = App.Services.GetRequiredService<UsageViewModel>();

    private TerminalViewModel Terminal { get; } = App.Services.GetRequiredService<TerminalViewModel>();

    /// <summary>
    /// 사람이 옮겨 다니는 자리를 모두 한 이력에 모은다 — 좌측 메뉴, 도구 탭, 터미널 방.
    /// 페이지 이동은 <see cref="Navigate"/>가 적고, 나머지는 싱글턴 뷰모델·방 등록부를 지켜본다.
    /// </summary>
    private void WatchSpots()
    {
        Dashboard.PropertyChanged += (_, e) => RecordIf(e.PropertyName, nameof(DashboardViewModel.SelectedTabIndex));
        Usage.PropertyChanged += (_, e) => RecordIf(e.PropertyName, nameof(UsageViewModel.SelectedTabIndex));
        Terminal.PropertyChanged += (_, e) => RecordIf(e.PropertyName, nameof(TerminalViewModel.SelectedToolIndex));
        App.Rooms.PropertyChanged += (_, e) => RecordIf(e.PropertyName, nameof(ViewModels.RoomManager.ActiveRoom));

        // 닫힌 방을 가리키는 자리는 걷어낸다. 안 그러면 뒤로가기가 없어진 방으로 간다
        App.Rooms.Rooms.CollectionChanged += (_, _) =>
            _history.Forget(spot => spot.Room is ViewModels.IRoom room && !App.Rooms.Rooms.Contains(room));

        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnRootPointerPressed),
            handledEventsToo: true);

        UpdateHistoryButtons();
    }

    private void RecordIf(string? changed, string watched)
    {
        if (changed == watched)
        {
            RecordSpot();
        }
    }

    private void RecordSpot()
    {
        _history.Record(CurrentSpot());
        UpdateHistoryButtons();
    }

    /// <summary>갈 곳이 없으면 잠근다. 잠긴 이유는 화살표 방향이 말한다.</summary>
    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Move(_history.GoBack());

    private void OnForwardClick(object sender, RoutedEventArgs e) => Move(_history.GoForward());

    /// <summary>좌측 메뉴를 접거나 편다. NavigationView 의 햄버거는 숨겼으므로 제목줄의 이 버튼이 그 일을 한다.</summary>
    private void OnPaneToggleClick(object sender, RoutedEventArgs e) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void Move(NavigationSpot? target)
    {
        if (target is not null)
        {
            Apply(target);
        }

        UpdateHistoryButtons();
    }

    private NavigationSpot CurrentSpot() => new(
        _pageTag,
        _pageTag switch
        {
            "Dashboard" => Dashboard.SelectedTabIndex,
            "Usage" => Usage.SelectedTabIndex,
            "Terminal" => Terminal.SelectedToolIndex,
            _ => -1,
        },
        _pageTag == "Terminal" ? App.Rooms.ActiveRoom : null);

    /// <summary>마우스 엄지 버튼. 뒤로(XButton1) · 앞으로(XButton2).</summary>
    private void OnRootPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var kind = e.GetCurrentPoint(RootGrid).Properties.PointerUpdateKind;

        if (kind is not (Microsoft.UI.Input.PointerUpdateKind.XButton1Pressed
            or Microsoft.UI.Input.PointerUpdateKind.XButton2Pressed))
        {
            return;
        }

        Move(kind == Microsoft.UI.Input.PointerUpdateKind.XButton1Pressed ? _history.GoBack() : _history.GoForward());
        e.Handled = true;
    }

    /// <summary>이력의 한 자리로 되돌린다. 되돌리는 동안은 새 자리로 적히지 않는다.</summary>
    private void Apply(NavigationSpot spot)
    {
        using (_history.Restoring())
        {
            NavigateTo(spot.PageTag);

            switch (spot.PageTag)
            {
                case "Dashboard":
                    Dashboard.SelectedTabIndex = spot.TabIndex;
                    break;
                case "Usage":
                    Usage.SelectedTabIndex = spot.TabIndex;
                    break;
                case "Terminal":
                    Terminal.SelectedToolIndex = spot.TabIndex;
                    App.Rooms.ActiveRoom = spot.Room as ViewModels.IRoom;
                    break;
                default:
                    break;
            }
        }
    }

    private void ApplyStatus()
    {
        // 알림이 있으면 그것을 먼저 보여 준다. 실패는 진행 상황보다 급하다
        var notice = _viewModel.ErrorMessage;
        var hasNotice = !string.IsNullOrWhiteSpace(notice);

        // 보여줄 상태가 없으면 바 자체를 숨긴다. 빈 줄이 화면 아래를 먹지 않게.
        var hasStatus = hasNotice || !string.IsNullOrWhiteSpace(_viewModel.StatusMessage) || _viewModel.IsIndexing;
        StatusBar.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = hasNotice ? notice : _viewModel.StatusMessage;
        StatusText.Foreground = hasNotice
            ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        StatusProgress.Visibility = _viewModel.IsIndexing ? Visibility.Visible : Visibility.Collapsed;
        StatusProgress.IsIndeterminate = _viewModel.StatusPercent <= 0;
        StatusProgress.Value = _viewModel.StatusPercent;
    }

    /// <summary>
    /// 테마를 적용한다. "System"이면 OS를 따르고 Mica를 쓴다.
    /// Light/Dark를 고른 경우 Mica는 OS 테마 색으로 남아 글자와 어긋나므로,
    /// 배경을 그 테마의 색으로 직접 칠하고 제목줄 단추 색도 맞춘다.
    /// </summary>
    public void ApplyTheme(string? theme = null)
    {
        var name = theme ?? _settings.Current.Theme;

        RootGrid.RequestedTheme = name switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (RootGrid.RequestedTheme == ElementTheme.Default)
        {
            SystemBackdrop = new MicaBackdrop();
            RootGrid.Background = null;
            ResetTitleBarColors();

            return;
        }

        var dark = RootGrid.RequestedTheme == ElementTheme.Dark;

        // Mica는 OS 테마를 따르므로 명시적 테마에서는 끄고 단색으로 칠한다.
        SystemBackdrop = null;
        RootGrid.Background = new SolidColorBrush(ChromeColor(dark, "Background"));

        ApplyTitleBarColors(dark);
    }

    /// <summary>
    /// 제목줄을 앱 화면 안으로 끌어온다. Windows 10에서는 제목줄 색을 바꿀 수 없어
    /// 시스템이 그리는 밝은 띠가 남기 때문이다. TitleBar 컨트롤이 끌기 영역과 버튼 자리(뒤로·LeftHeader)의
    /// 입력 통과 영역을 스스로 맞춘다.
    /// </summary>
    private void ExtendTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void ApplyTitleBarColors(bool dark)
    {
        var bar = AppWindow.TitleBar;
        var foreground = dark ? Colors.White : Colors.Black;
        var background = ChromeColor(dark, "Background");
        var hover = ChromeColor(dark, "Hover");

        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonBackgroundColor = background;
        bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonHoverBackgroundColor = hover;
        bar.BackgroundColor = background;
        bar.InactiveBackgroundColor = background;
        bar.ForegroundColor = foreground;
    }

    /// <summary>창 바탕·제목줄 색. 값의 정본은 App.xaml 이다 — 색을 바꾸려면 그 한 곳만 고친다.</summary>
    private static Color ChromeColor(bool dark, string role) =>
        (Color)Application.Current.Resources[(dark ? "WindowChromeDark" : "WindowChromeLight") + role];

    private void ResetTitleBarColors()
    {
        var bar = AppWindow.TitleBar;

        bar.ButtonForegroundColor = null;
        bar.ButtonInactiveForegroundColor = null;
        bar.ButtonHoverForegroundColor = null;
        bar.ButtonBackgroundColor = null;
        bar.ButtonInactiveBackgroundColor = null;
        bar.ButtonHoverBackgroundColor = null;
        bar.BackgroundColor = null;
        bar.InactiveBackgroundColor = null;
        bar.ForegroundColor = null;
    }

    /// <summary>제목줄과 작업 표시줄 아이콘.</summary>
    private void ApplyIcon()
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "daiso.ico");

        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }
    }

    /// <summary>목록과 상세를 나란히 보는 화면이라 이보다 좁으면 쓸 수 없다.</summary>
    private const int MinimumWidth = 1024;

    private const int MinimumHeight = 700;

    private void RestoreWindowSize()
    {
        var width = Math.Max(MinimumWidth, _settings.Current.WindowWidth);
        var height = Math.Max(MinimumHeight, _settings.Current.WindowHeight);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        // 사용자가 더 줄이지 못하게 창 자체의 하한을 준다. 레이아웃이 깨질 구간을 없앤다.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }
    }

    /// <summary>설정에서 테마를 바꾸면 창이 바로 따라간다.</summary>
    private void WatchThemeSetting()
    {
        var settings = App.Services.GetRequiredService<ViewModels.SettingsViewModel>();

        settings.ThemeChanged += (_, theme) =>
            RootGrid.DispatcherQueue.TryEnqueue(() => ApplyTheme(theme));
    }

    private bool _closeConfirmed;

    /// <summary>
    /// 앱을 닫을 때 살아 있는 대화 방이 있으면 한 번 묻는다. 계속 닫으면 방을 정리해 자식 CLI를 끝낸다.
    /// AppWindow.Closing은 취소할 수 있어(Window.Closed는 못 한다) 여기서 확인 대화를 띄운다.
    /// </summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !App.Rooms.HasRooms)
        {
            App.Rooms.DisposeAll();
            return;
        }

        args.Cancel = true;

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = UiStrings.Get("Room_CloseTitle"),
            Content = new TextBlock { Text = UiStrings.Get("Room_CloseBody"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = UiStrings.Get("Room_CloseAndQuit"),
            CloseButtonText = UiStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _closeConfirmed = true;
            App.Rooms.DisposeAll();
            Close();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.Rooms.DisposeAll();
        _settings.Current.WindowWidth = AppWindow.Size.Width;
        _settings.Current.WindowHeight = AppWindow.Size.Height;
        _settings.Save();

        // 설정을 쓴 다음에 컨테이너를 닫는다 — 싱글턴 중에 IDisposable 이 있다.
        // 특히 SqliteSessionIndex 는 쓰기 연결을 열어 두고 있어, 닫지 않으면 WAL 체크포인트가 돌지 않는다.
        App.DisposeServices();
    }
}

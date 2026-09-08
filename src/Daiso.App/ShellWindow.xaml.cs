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

        Navigate("Dashboard");
        _viewModel.StartBackgroundRefresh();

        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>다른 페이지의 안내에서 이 페이지로 보내 달라고 할 때 쓴다. 왼쪽 선택 표시도 맞춘다.</summary>
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
    }

    private void ApplyStatus()
    {
        // 보여줄 상태가 없으면 바 자체를 숨긴다. 빈 줄이 화면 아래를 먹지 않게.
        var hasStatus = !string.IsNullOrWhiteSpace(_viewModel.StatusMessage) || _viewModel.IsIndexing;
        StatusBar.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = _viewModel.StatusMessage;
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
        RootGrid.Background = new SolidColorBrush(dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243));

        ApplyTitleBarColors(dark);
    }

    /// <summary>
    /// 제목줄을 앱 화면 안으로 끌어온다. Windows 10에서는 제목줄 색을 바꿀 수 없어
    /// 시스템이 그리는 밝은 띠가 남기 때문이다. 끌어온 영역은 창 드래그로 쓴다.
    /// </summary>
    private void ExtendTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
    }

    private void ApplyTitleBarColors(bool dark)
    {
        var bar = AppWindow.TitleBar;
        var foreground = dark ? Colors.White : Colors.Black;
        var background = dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243);
        var hover = dark
            ? Color.FromArgb(255, 58, 58, 58)
            : Color.FromArgb(255, 226, 226, 226);

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
    }
}

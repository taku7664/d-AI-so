using Daiso.App.Services;
using Daiso.App.ViewModels;
using Daiso.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

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

        ApplyTheme();
        RestoreWindowSize();
        ApplyIcon();

        Navigate("Dashboard");
        _viewModel.StartBackgroundRefresh();

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
            "Terminal" => typeof(TerminalPage),
            "Sessions" => typeof(SessionsPage),
            "RuleMaker" => typeof(RuleMakerPage),
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

    /// <summary>설정의 테마를 적용한다. "System"이면 손대지 않아 OS 설정을 따른다.</summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _settings.Current.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
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

    private void RestoreWindowSize()
    {
        var width = Math.Max(800, _settings.Current.WindowWidth);
        var height = Math.Max(600, _settings.Current.WindowHeight);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _settings.Current.WindowWidth = AppWindow.Size.Width;
        _settings.Current.WindowHeight = AppWindow.Size.Height;
        _settings.Save();
    }
}

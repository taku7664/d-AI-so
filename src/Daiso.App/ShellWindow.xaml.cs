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

        Navigate("Dashboard");
        _viewModel.StartBackgroundRefresh();

        Closed += OnClosed;
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

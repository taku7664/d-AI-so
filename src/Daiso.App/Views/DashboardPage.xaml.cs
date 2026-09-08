using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        Shell.PropertyChanged += OnShellPropertyChanged;

        Loaded += async (_, _) =>
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            SyncProfiles();
        };
    }

    /// <summary>전체 프로필 목록을 도구 카드마다 자기 도구 것만 골라 넣는다.</summary>
    private void SyncProfiles()
    {
        foreach (var card in ViewModel.Tools)
        {
            card.SetProfiles(Profiles.Profiles);
        }
    }

    /// <summary>작업 결과를 그 도구 카드의 팝오버에만 적는다.</summary>
    private void ReportProfile(Daiso.Core.ToolKind tool)
    {
        SyncProfiles();

        foreach (var card in ViewModel.Tools)
        {
            card.ProfileStatus = card.Kind == tool ? Profiles.StatusText : null;
        }
    }

    public DashboardViewModel ViewModel { get; }

    /// <summary>로그인 프로필 카드.</summary>
    public AuthProfileViewModel Profiles { get; } = App.Services.GetRequiredService<AuthProfileViewModel>();

    /// <summary>인덱싱 진행 상태. 끝나면 요약을 다시 읽는다.</summary>
    private ShellViewModel Shell { get; } = App.Services.GetRequiredService<ShellViewModel>();

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsIndexing) && !Shell.IsIndexing)
        {
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
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

    /// <summary>세션 목록으로 보낸다.</summary>
    private void OnGoToSessionsClick(object sender, RoutedEventArgs e) => Go("Sessions");

    /// <summary>사용량 화면으로 보낸다. 요약의 토큰 수치는 거기서 자세히 본다.</summary>
    private void OnGoToUsageClick(object sender, RoutedEventArgs e) => Go("Usage");

    private static void Go(string tag) => (App.MainWindow as ShellWindow)?.NavigateTo(tag);

    /// <summary>최근 세션을 Terminal 화면에 채워 넣고 그 화면으로 보낸다.</summary>
    private void OnResumeRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecentSessionViewModel row })
        {
            return;
        }

        ViewModel.PrepareResume(row.Session);
        Go("Terminal");
    }

    /// <summary>지금 로그인 상태를 이름 붙여 저장한다. 누른 카드의 도구로 저장하니 이름만 받는다.</summary>
    private async void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolCardViewModel card })
        {
            return;
        }

        var name = new TextBox
        {
            Header = UiStrings.Format("AuthProfile_SaveBodyFor", card.Title),
            PlaceholderText = UiStrings.Get("AuthProfile_NamePlaceholder"),
        };

        var panel = new StackPanel { Width = 360 };
        panel.Children.Add(name);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Get("AuthProfile_SaveTitle"),
            Content = panel,
            PrimaryButtonText = UiStrings.Get("Common_Save"),
            CloseButtonText = UiStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name.Text))
        {
            return;
        }

        try
        {
            var status = await ViewModel.ReadAuthStatusAsync(card.Kind);
            Profiles.Save(card.Kind, name.Text, status);
            ReportProfile(card.Kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowAsync(UiStrings.Get("AuthProfile_SaveFailed"), ex.Message);
        }
    }

    /// <summary>고른 프로필을 현재 로그인으로 되돌린다.</summary>
    private async void OnUseProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AuthProfileRowViewModel row })
        {
            return;
        }

        try
        {
            Profiles.Apply(row, await ViewModel.ReadAuthStatusAsync(row.Profile.Tool));
            await ViewModel.LoadCommand.ExecuteAsync(null);
            ReportProfile(row.Profile.Tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowAsync(UiStrings.Get("AuthProfile_ApplyFailed"), ex.Message);
        }
    }

    /// <summary>프로필을 지운다. 지금 로그인은 건드리지 않는다.</summary>
    private async void OnRemoveProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AuthProfileRowViewModel row })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Format("AuthProfile_ConfirmRemove", row.Name),
            Content = new TextBlock
            {
                Text = UiStrings.Get("AuthProfile_ConfirmRemoveBody"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = UiStrings.Get("Common_Delete"),
            CloseButtonText = UiStrings.Get("Common_Cancel"),
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Profiles.Remove(row);
            ReportProfile(row.Profile.Tool);
        }
    }

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = UiStrings.Get("Common_Close"),
    }.ShowAsync().AsTask();

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolCardViewModel card })
        {
            await ViewModel.LoginCommand.ExecuteAsync(card);
        }
    }
}

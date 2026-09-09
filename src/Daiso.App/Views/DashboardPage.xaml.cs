using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);
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

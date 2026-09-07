using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;

namespace Daiso.App.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
        Doctor = App.Services.GetRequiredService<ContextDoctorViewModel>();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    public SessionsViewModel ViewModel { get; }

    /// <summary>오른쪽 컨텍스트 탭.</summary>
    public ContextDoctorViewModel Doctor { get; }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionsViewModel.HasSearchResults):
                RebuildSearchTree();
                break;

            // 프로젝트를 바꾸면 컨텍스트 리포트도 그 폴더로 맞춘다.
            case nameof(SessionsViewModel.SelectedProject):
                _ = Doctor.SetProjectAsync(ViewModel.SelectedProject?.Path);
                break;

            default:
                break;
        }
    }

    /// <summary>검색 결과를 프로젝트 &gt; 세션 &gt; 매칭 문장 트리로 옮긴다.</summary>
    private void RebuildSearchTree()
    {
        SearchTree.RootNodes.Clear();

        foreach (var project in ViewModel.SearchResults)
        {
            var projectNode = new TreeViewNode { Content = project.Summary, IsExpanded = true };

            foreach (var session in project.Sessions)
            {
                var sessionNode = new TreeViewNode { Content = session.Summary, IsExpanded = false };

                foreach (var match in session.Matches)
                {
                    sessionNode.Children.Add(new TreeViewNode { Content = match });
                }

                projectNode.Children.Add(sessionNode);
            }

            SearchTree.RootNodes.Add(projectNode);
        }
    }

    private async void OnSearchItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode { Content: SearchMatchViewModel match })
        {
            await ViewModel.SelectFromSearchAsync(match.Session);
        }
    }

    private async void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
    }

    private async void OnSessionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedSession is { } row)
        {
            await ViewModel.LoadTimelineAsync(row.Session);
        }
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ResumeCommand.ExecuteAsync(ViewModel.SelectedSession);

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSession is not { } row)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{row.Session.Tool}-{row.Session.Id}",
        };
        picker.FileTypeChoices.Add("Markdown", [".md"]);

        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        if (await picker.PickSaveFileAsync() is { } file)
        {
            await ViewModel.ExportAsync(row.Session, file.Path);
        }
    }

    private void OnSelectOldClick(object sender, RoutedEventArgs e) =>
        ViewModel.SelectByRuleCommand.Execute("old");

    private void OnSelectOrphanClick(object sender, RoutedEventArgs e) =>
        ViewModel.SelectByRuleCommand.Execute("orphan");

    private void OnSelectLargeClick(object sender, RoutedEventArgs e) =>
        ViewModel.SelectByRuleCommand.Execute("large");

    private async void OnRecycleClick(object sender, RoutedEventArgs e) => await DeleteAsync(permanent: false);

    private async void OnPermanentClick(object sender, RoutedEventArgs e) => await DeleteAsync(permanent: true);

    /// <summary>삭제 전 미리보기를 띄우고, 영구 삭제는 한 번 더 묻는다. (REQUIREMENTS §5.2)</summary>
    private async Task DeleteAsync(bool permanent)
    {
        var preview = ViewModel.BuildDeletePreview();

        if (preview.Count == 0)
        {
            await ShowAsync("삭제할 세션 없음", "먼저 세션을 선택하세요.");
            return;
        }

        var skipped = preview.ActiveSessionIds.Count > 0
            ? $"\n\n실행 중이라 제외되는 세션 {preview.ActiveSessionIds.Count}건:\n"
                + string.Join("\n", preview.ActiveSessionIds)
            : string.Empty;

        var body =
            $"선택 {preview.Count}건\n"
            + $"회수 용량 {DashboardViewModel.FormatSize(preview.ReclaimBytes)}\n"
            + $"방식: {(permanent ? "영구 삭제" : "휴지통으로 이동")}"
            + skipped;

        if (!await ConfirmAsync(permanent ? "영구 삭제 미리보기" : "삭제 미리보기", body, "계속"))
        {
            return;
        }

        if (permanent && !await ConfirmAsync(
            "정말 영구 삭제할까요?",
            "휴지통을 거치지 않으므로 되돌릴 수 없다.",
            "영구 삭제"))
        {
            return;
        }

        var result = await ViewModel.DeleteCheckedAsync(permanent);

        var report = $"삭제 {result.Deleted.Count}건";
        if (result.Skipped.Count > 0)
        {
            report += "\n\n건너뜀:\n" + string.Join(
                "\n",
                result.Skipped.Select(item => $"{Path.GetFileName(item.Path)} — {item.Reason}"));
        }

        await ShowAsync("삭제 결과", report);
    }

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "닫기",
    }.ShowAsync().AsTask();

    private async Task<bool> ConfirmAsync(string title, string body, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

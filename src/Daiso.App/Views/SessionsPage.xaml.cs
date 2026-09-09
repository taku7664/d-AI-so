using Daiso.App;
using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;
using Daiso.App.Strings;

namespace Daiso.App.Views;

public sealed partial class SessionsPage : Page, IPageHeaderSource
{
    public SessionsPage()
    {
        InitializeComponent();
        FocusRelease.Attach(this);
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
        Header = new PageHeader("Sessions_Title", UiStrings.Get("Sessions_Subtitle"));
        Shell.PropertyChanged += OnShellPropertyChanged;
        Doctor = App.Services.GetRequiredService<ContextDoctorViewModel>();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SelectorBarVisuals.ResetPressedOnLeave(ToolTabs);

        Loaded += async (_, _) =>
        {
            SelectorBarVisuals.Select(ToolTabs, ViewModel.ToolFilterIndex);
            await UiCommands.RunAsync(ViewModel.LoadCommand);
        };
    }

    /// <summary>도구 탭을 누르면 목록을 그 도구로 거른다. 탭 순서 = ToolFilters 순서(전체 → ToolLook.DisplayOrder).</summary>
    private void OnToolTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);

        if (index >= 0 && index != ViewModel.ToolFilterIndex)
        {
            ViewModel.ToolFilterIndex = index;
        }
    }

    public SessionsViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    /// <summary>첫 실행 인덱싱 진행을 빈 목록 안내에 함께 보여준다.</summary>
    public ShellViewModel Shell { get; } = App.Services.GetRequiredService<ShellViewModel>();

    /// <summary>
    /// 인덱싱이 끝나면 목록을 자동으로 다시 읽는다.
    /// 첫 실행에는 인덱싱 도중 목록을 그린 상태라 그대로 두면 옛 숫자가 남는다.
    /// </summary>
    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsIndexing) && !Shell.IsIndexing)
        {
            UiCommands.Start(ViewModel.LoadCommand);
        }
    }

    /// <summary>오른쪽 컨텍스트 탭.</summary>
    public ContextDoctorViewModel Doctor { get; }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionsViewModel.HasSearchResults):
                RebuildSearchTree();
                break;

            // 필터 초기화처럼 뷰모델 쪽에서 바뀌면 탭 띠도 따라간다 (양방향, ARCHITECTURE §6.2)
            case nameof(SessionsViewModel.ToolFilterIndex):
                SelectorBarVisuals.Select(ToolTabs, ViewModel.ToolFilterIndex);
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

    /// <summary>Ctrl+F는 검색란으로 커서를 옮긴다.</summary>
    private void OnFocusSearchInvoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    /// <summary>Esc는 검색을 지운다.</summary>
    private void OnClearSearchInvoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ClearSearchCommand.Execute(null);
        args.Handled = true;
    }

    private async void OnSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await UiCommands.RunAsync(ViewModel.SearchCommand);
        }
    }

    private async void OnSessionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedSession is { } row)
        {
            await ViewModel.LoadTimelineAsync(row.Session);
        }
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSession is not { } row)
        {
            return;
        }

        // WebView2가 있으면 앱 안 방으로 연다. 없으면 기존처럼 외부 터미널로
        if (Terminal.TerminalHost.IsRuntimeAvailable())
        {
            var provider = App.Services.GetRequiredService<IEnumerable<Daiso.Core.IProvider>>()
                .First(p => p.Kind == row.Session.Tool);
            var directory = row.Session.ProjectPath is { Length: > 0 } path && Directory.Exists(path)
                ? path
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            App.Services.GetRequiredService<TerminalViewModel>()
                .PrepareResume(row.Session.Tool, directory, provider.BuildResumeArguments(row.Session), autoOpen: true);

            (App.MainWindow as ShellWindow)?.NavigateTo("Terminal");
            return;
        }

        await ViewModel.ResumeCommand.ExecuteAsync(row);
    }

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

    /// <summary>목록 머리의 전체 체크. 하나라도 안 체크된 것이 있으면 전부 켜고, 전부 켜져 있으면 전부 끈다.</summary>
    private void OnCheckAllClick(object sender, RoutedEventArgs e) =>
        ViewModel.CheckAll(sender is CheckBox { IsChecked: true });

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
            await ShowAsync(
                UiStrings.Get("Sessions_NoSelection"),
                UiStrings.Get("Sessions_SelectFirst"));
            return;
        }

        var skipped = preview.ActiveSessionIds.Count > 0
            ? "\n\n" + UiStrings.Format("Sessions_ActiveExcluded", preview.ActiveSessionIds.Count)
                + "\n" + string.Join("\n", preview.ActiveSessionIds)
            : string.Empty;

        var body = UiStrings.Format(
            "Sessions_DeletePreviewBody",
            preview.Count,
            DashboardViewModel.FormatSize(preview.ReclaimBytes),
            UiStrings.Get(permanent ? "Common_PermanentDelete" : "Sessions_MoveToRecycleBin"))
            + skipped;

        if (!await ConfirmAsync(
            UiStrings.Get(permanent ? "Sessions_PermanentPreviewTitle" : "Sessions_DeletePreviewTitle"),
            body,
            UiStrings.Get("Common_Continue")))
        {
            return;
        }

        if (permanent && !await ConfirmAsync(
            UiStrings.Get("Sessions_ConfirmPermanentTitle"),
            UiStrings.Get("Sessions_ConfirmPermanentBody"),
            UiStrings.Get("Common_PermanentDelete")))
        {
            return;
        }

        var result = await ViewModel.DeleteCheckedAsync(permanent);

        var report = UiStrings.Format("Sessions_DeletedCount", result.Deleted.Count);
        if (result.Skipped.Count > 0)
        {
            report += "\n\n" + UiStrings.Get("Sessions_SkippedHeader") + "\n" + string.Join(
                "\n",
                result.Skipped.Select(item => $"{Path.GetFileName(item.Path)} — {item.Reason}"));
        }

        await ShowAsync(UiStrings.Get("Sessions_DeleteResultTitle"), report);
    }

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = UiStrings.Get("Common_Close"),
    }.ShowAsync().AsTask();

    private async Task<bool> ConfirmAsync(string title, string body, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = UiStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

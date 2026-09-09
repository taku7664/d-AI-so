using Daiso.App.Controls;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Daiso.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Daiso.App.Views;

public sealed partial class RuleMakerPage : Page, IPageHeaderSource
{
    public RuleMakerPage()
    {
        InitializeComponent();
        FocusRelease.Attach(this);
        ViewModel = App.Services.GetRequiredService<RuleMakerViewModel>();
        Header = new PageHeader("RuleMaker_Title", UiStrings.Get("RuleMaker_Subtitle"));
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += (_, _) =>
        {
            if (ViewModel.Rules.Count == 0)
            {
                ViewModel.NewCommand.Execute(null);
            }

            BindTree();
            ViewModel.RefreshGalleryCommand.Execute(null);
            BuildRecentFlyout();
        };
    }

    public RuleMakerViewModel ViewModel { get; }

    /// <summary>셸이 NavigationView.Header 에 그리는 대제목·부제.</summary>
    public PageHeader Header { get; }

    /// <summary>지금 트리에서 고른 노드. 없으면 루트.</summary>
    private ConditionNodeViewModel? SelectedNode =>
        ConditionTree.SelectedItem as ConditionNodeViewModel ?? ViewModel.SelectedRule?.Root;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RuleMakerViewModel.SelectedRule):
                BindTree();
                break;
            case nameof(RuleMakerViewModel.CurrentPath):
                BuildRecentFlyout();
                break;

            default:
                break;
        }
    }

    private void BindTree() => ConditionTree.ItemsSource = ViewModel.SelectedRule?.Roots;

    // ── 파일 ─────────────────────────────────────────────────────────────

    /// <summary>편집기에 실려 있는 목록 항목. 같은 것을 다시 고르면 묻지 않는다.</summary>
    private PresetGalleryItemViewModel? _loadedGalleryItem;

    /// <summary>확인을 거절해 선택을 되돌리는 중인가. 그때 다시 들어오는 이벤트는 무시한다.</summary>
    private bool _revertingSelection;

    /// <summary>저장하지 않은 편집이 있으면 버릴지 묻는다. true면 진행.</summary>
    private async Task<bool> CanLeaveAsync() => !ViewModel.IsDirty || await DiscardDialog.ConfirmAsync(XamlRoot);

    /// <summary>
    /// 목록에서 고르면 편집기에 싹 실린다. 편집 중인 것이 있으면 먼저 묻고, 거절하면 선택을 이전 항목으로 되돌린다.
    /// </summary>
    private async void OnGallerySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_revertingSelection || ViewModel.SelectedGalleryItem is not { } item)
        {
            return;
        }

        if (_loadedGalleryItem?.Key == item.Key)
        {
            // 목록을 다시 채우며 같은 항목을 되찾은 것. 편집기는 그대로
            _loadedGalleryItem = item;
            return;
        }

        if (!await CanLeaveAsync())
        {
            _revertingSelection = true;
            ViewModel.SelectedGalleryItem = _loadedGalleryItem;
            _revertingSelection = false;
            return;
        }

        ViewModel.OpenFromGallery(item);
        _loadedGalleryItem = item;
        BindTree();
    }

    /// <summary>목록 선택을 비운다. 파일을 열거나 새로 만들면 목록의 어느 것도 편집기에 실린 게 아니다.</summary>
    private void ClearGallerySelection()
    {
        _revertingSelection = true;
        ViewModel.SelectedGalleryItem = null;
        _loadedGalleryItem = null;
        _revertingSelection = false;
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        ViewModel.NewCommand.Execute(null);
        ClearGallerySelection();
        BindTree();
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".daiso");
        Attach(picker);

        if (await picker.PickSingleFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            ViewModel.Open(file.Path);
            BindTree();
            BuildRecentFlyout();
        }
        catch (RuleParseException ex)
        {
            await ShowFileParseErrorAsync(ex);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentPath is { Length: > 0 } path)
        {
            await SaveToAsync(path);
            return;
        }

        await SaveAsAsync();
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = InstructionTemplate.DefaultRulesFileName,
        };
        picker.FileTypeChoices.Add(UiStrings.Get("RuleMaker_FileTypeLabel"), [".daiso"]);
        Attach(picker);

        if (await picker.PickSaveFileAsync() is { } file)
        {
            await SaveToAsync(file.Path);
        }
    }

    private async Task SaveToAsync(string path)
    {
        try
        {
            ViewModel.Save(path);
            BuildRecentFlyout();
        }
        catch (RuleParseException ex)
        {
            await ShowParseErrorAsync(ex);
        }
    }

    private async void OnLinkClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        Attach(picker);

        if (await picker.PickSingleFolderAsync() is not { } folder)
        {
            return;
        }

        try
        {
            ViewModel.LinkToProject(folder.Path);
        }
        catch (RuleParseException ex)
        {
            await ShowParseErrorAsync(ex);
        }
    }

    private async void OnLinkTypedClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.LinkToTypedProjectCommand.Execute(null);
        }
        catch (RuleParseException ex)
        {
            await ShowParseErrorAsync(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(UiStrings.Get("RuleMaker_LinkFailed"), ex.Message);
        }
    }

    /// <summary>
    /// 앱이 아는 프로젝트를 목록으로 보여준다. 세션 인덱스에 있는 폴더라 대화상자를 열 필요가 없다.
    /// 목록에 없는 폴더만 "다른 폴더 고르기"로 간다.
    /// </summary>
    private async void OnProjectFlyoutOpening(object? sender, object e)
    {
        await ViewModel.LoadProjectChoicesAsync();

        ProjectFlyout.Items.Clear();

        var labels = Formats.Labels([.. ViewModel.ProjectChoices]);

        for (var i = 0; i < ViewModel.ProjectChoices.Count; i++)
        {
            var path = ViewModel.ProjectChoices[i];
            var item = new MenuFlyoutItem
            {
                Text = labels[i],
                Icon = new SymbolIcon(Symbol.Folder),
            };

            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => ViewModel.ProjectDirectory = path;
            ProjectFlyout.Items.Add(item);
        }

        if (ProjectFlyout.Items.Count == 0)
        {
            ProjectFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = UiStrings.Get("Common_NoKnownProjects"),
                IsEnabled = false,
            });
        }

        ProjectFlyout.Items.Add(new MenuFlyoutSeparator());

        var browse = new MenuFlyoutItem { Text = UiStrings.Get("Common_BrowseOther") };
        browse.Click += async (_, _) => await PickProjectAsync();
        ProjectFlyout.Items.Add(browse);
    }

    private async Task<string?> PickProjectAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        Attach(picker);

        if (await picker.PickSingleFolderAsync() is not { } folder)
        {
            return null;
        }

        ViewModel.ProjectDirectory = folder.Path;

        return folder.Path;
    }

    // ── 지시문 마이그레이션 (REQUIREMENTS §7) ────────────────────────────

    /// <summary>좌우 diff를 보여 주고 방향을 고르게 한다. 고르기 전에는 아무것도 쓰지 않는다.</summary>
    private async void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        // 경로가 비어 있으면 바로 폴더 선택 대화상자를 띄운다.
        if (ViewModel.ProjectDirectory is not { Length: > 0 } directory)
        {
            if (await PickProjectAsync() is not { Length: > 0 } picked)
            {
                return;
            }

            directory = picked;
        }

        var migration = App.Services.GetRequiredService<MigrationViewModel>();

        try
        {
            migration.Inspect(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(UiStrings.Get("RuleMaker_ReadFailed"), ex.Message);
            return;
        }

        // 방향은 이제 둘이 아니다. 고를 수 있는 것을 목록으로 주고 하나를 고르게 한다
        var picker = new ComboBox
        {
            ItemsSource = migration.Directions,
            DisplayMemberPath = nameof(MigrationDirectionViewModel.Label),
            SelectedIndex = migration.Directions.Count > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // AutomationProperties 는 붙임 속성이라 개체 초기자로는 못 준다
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(picker, "MigrationDirectionPicker");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, UiStrings.Get("Migration_DialogTitle"));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Get("Migration_DialogTitle"),
            Content = BuildMigrationView(migration, picker),
            PrimaryButtonText = UiStrings.Get("Migration_Run"),
            CloseButtonText = UiStrings.Get("Common_Close"),
            IsPrimaryButtonEnabled = migration.CanMigrate,
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || picker.SelectedItem is not MigrationDirectionViewModel chosen)
        {
            return;
        }

        try
        {
            var result = migration.Apply(chosen.Direction);
            var target = migration.TargetFileName(result.Target);

            await ShowAsync(
                UiStrings.Format("Migration_Updated", target),
                result.Warnings.Count == 0
                    ? UiStrings.Get("Migration_NoWarnings")
                    : string.Join('\n', result.Warnings.Select(MigrationViewModel.Render)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowAsync(UiStrings.Get("Migration_Failed"), ex.Message);
        }
    }

    private static FrameworkElement BuildMigrationView(MigrationViewModel migration, ComboBox picker)
    {
        var panel = new StackPanel { Spacing = 8, Width = 640 };

        panel.Children.Add(picker);

        panel.Children.Add(new TextBlock
        {
            Text = migration.Notes,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = UiStrings.Get("Migration_DiffHint"),
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });

        panel.Children.Add(new ListView
        {
            ItemsSource = migration.Diff,
            DisplayMemberPath = nameof(MigrationDiffLineViewModel.Display),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 360,
        });

        return panel;
    }

    private void BuildRecentFlyout()
    {
        RecentFlyout.Items.Clear();

        foreach (var path in ViewModel.RecentFiles)
        {
            var item = new MenuFlyoutItem { Text = path };
            item.Click += async (_, _) => await OpenPathAsync(path);
            RecentFlyout.Items.Add(item);
        }

        if (RecentFlyout.Items.Count == 0)
        {
            RecentFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = UiStrings.Get("RuleMaker_NoRecent"),
                IsEnabled = false,
            });
        }
    }

    // ── 프리셋 갤러리 ─────────────────────────────────────────────────────

    /// <summary>목록 항목의 접근성 이름을 프리셋 이름으로.</summary>
    private void OnGalleryContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is PresetGalleryItemViewModel item)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, item.Name);
        }
    }

    /// <summary>왼쪽 목록을 접거나 편다. 좁은 창에서 편집기에 자리를 내준다.</summary>
    private void OnToggleListClick(object sender, RoutedEventArgs e)
    {
        var show = sender is AppBarToggleButton { IsChecked: true };

        if (!show && ListColumn.ActualWidth > 0)
        {
            _listWidth = ListColumn.ActualWidth;
        }

        ListColumn.Width = show ? new GridLength(Math.Max(ListPaneMinWidth, _listWidth)) : new GridLength(0);
        ListColumn.MinWidth = show ? ListPaneMinWidth : 0;
        ListSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>목록 칸 최소 폭. XAML 의 ListPaneMinWidth 와 같은 리소스를 읽는다 (세 2단 화면이 같은 값).</summary>
    private static double ListPaneMinWidth => (double)Application.Current.Resources["ListPaneMinWidth"];

    private double _listWidth = ((GridLength)Application.Current.Resources["ListPaneWidth"]).Value;

    /// <summary>
    /// 본문 최소 폭 + 미리보기 최소 폭 + 열 간격보다 좁으면 미리보기 칸을 접는다. 창 하한 1024에서 목록을 펴 두면 이 경우다.
    /// 판단 근거는 격자의 실제 폭이다. 미리보기 열에 MinWidth 를 두면 격자가 제 칸보다 커져 이 값이 거짓말을 하므로
    /// 최소 폭은 여기 상수로만 둔다 (XAML 주석 참고).
    /// </summary>
    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var body = EditorGrid.ColumnDefinitions[0];
        var fits = e.NewSize.Width >= body.MinWidth + PreviewMinWidth + EditorColumnSpacing;

        if (fits == (PreviewCard.Visibility == Visibility.Visible))
        {
            return;
        }

        PreviewCard.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = fits ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EditorGrid.ColumnSpacing = fits ? EditorColumnSpacing : 0;
    }

    /// <summary>미리보기 칸이 읽힐 최소 폭. 제목 "마크다운 미리보기"와 카드 안쪽 여백이 들어가는 값.</summary>
    private const double PreviewMinWidth = 190;

    /// <summary>XAML 의 EditorGrid ColumnSpacing 과 같은 값. 접을 때 0 으로 내렸다가 펼 때 되돌린다.</summary>
    private const double EditorColumnSpacing = 14;

    /// <summary>고른 프리셋의 규칙을 지금 규칙 뒤에 붙인다.</summary>
    private void OnGalleryMergeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGalleryItem is not { } item)
        {
            return;
        }

        ViewModel.MergeFromGallery(item);
        BindTree();
    }

    /// <summary>현재 프리셋을 내 라이브러리에 저장한다. 갤러리에 바로 나타난다.</summary>
    private async void OnSaveToLibraryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.SaveToLibraryCommand.Execute(null);
            ViewModel.RefreshGalleryCommand.Execute(null);
        }
        catch (RuleParseException ex)
        {
            await ShowParseErrorAsync(ex);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        if (!await CanLeaveAsync())
        {
            return;
        }

        try
        {
            ViewModel.Open(path);
            ClearGallerySelection();
            BindTree();
        }
        catch (RuleParseException ex)
        {
            await ShowFileParseErrorAsync(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(UiStrings.Get("RuleMaker_OpenFailed"), ex.Message);
        }
    }

    // ── Global 행동 ──────────────────────────────────────────────────────

    private void OnGlobalUpClick(object sender, RoutedEventArgs e) =>
        ViewModel.MoveGlobalActionUpCommand.Execute(Context<ActionEditViewModel>(sender));

    private void OnGlobalDownClick(object sender, RoutedEventArgs e) =>
        ViewModel.MoveGlobalActionDownCommand.Execute(Context<ActionEditViewModel>(sender));

    private void OnGlobalRemoveClick(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveGlobalActionCommand.Execute(Context<ActionEditViewModel>(sender));

    // ── 추가 뒤 포커스 ───────────────────────────────────────────────────
    //
    // "행동 추가"를 누르면 새 줄이 생기는데 포커스는 버튼에 남아 있었다. 그 자리에서 바로 타이핑하면
    // 글자는 버려지고, 문장 속 띄어쓰기가 버튼을 다시 눌러 빈 줄이 하나씩 더 생겼다.
    // 추가한 직후 처음 뜨는 입력칸으로 커서를 옮긴다. 플래그는 그 한 번만 산다.

    private bool _focusNextActionBox;

    private bool _focusNextConditionBox;

    private void OnActionBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (_focusNextActionBox && sender is TextBox box)
        {
            _focusNextActionBox = false;
            box.Focus(FocusState.Programmatic);
        }
    }

    private void OnConditionBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (_focusNextConditionBox && sender is TextBox box)
        {
            _focusNextConditionBox = false;
            box.Focus(FocusState.Programmatic);
        }
    }

    private void OnAddGlobalClick(object sender, RoutedEventArgs e)
    {
        _focusNextActionBox = true;
        ViewModel.AddGlobalAction();
    }

    /// <summary>새 규칙은 조건부터 적는다. 조건 칸이 뜨면 거기로 커서가 간다.</summary>
    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        _focusNextConditionBox = true;
        ViewModel.AddRule();
    }

    // ── 규칙 행동 ────────────────────────────────────────────────────────

    private void OnAddRuleActionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRule is { } rule)
        {
            _focusNextActionBox = true;
            rule.Actions.Add(new ActionEditViewModel());
            rule.NotifyChanged();
        }
    }

    private void OnRuleActionUpClick(object sender, RoutedEventArgs e) =>
        MoveRuleAction(Context<ActionEditViewModel>(sender), -1);

    private void OnRuleActionDownClick(object sender, RoutedEventArgs e) =>
        MoveRuleAction(Context<ActionEditViewModel>(sender), +1);

    private void OnRuleActionRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRule is { } rule && Context<ActionEditViewModel>(sender) is { } action)
        {
            rule.Actions.Remove(action);
            rule.NotifyChanged();
        }
    }

    private void MoveRuleAction(ActionEditViewModel? action, int delta)
    {
        if (ViewModel.SelectedRule is not { } rule || action is null)
        {
            return;
        }

        var index = rule.Actions.IndexOf(action);
        var target = index + delta;

        if (index >= 0 && target >= 0 && target < rule.Actions.Count)
        {
            rule.Actions.Move(index, target);
            rule.NotifyChanged();
        }
    }

    private void OnRemoveRuleClick(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveRuleCommand.Execute(ViewModel.SelectedRule);

    // ── 조건 트리 ────────────────────────────────────────────────────────

    private void OnAddLeafClick(object sender, RoutedEventArgs e)
    {
        _focusNextConditionBox = true;
        AddNode(ConditionNodeKind.Leaf);
    }

    /// <summary>연산자 노드에는 자식으로, 리프 옆에는 형제로 넣는다.</summary>
    private void AddNode(ConditionNodeKind kind)
    {
        if (ViewModel.SelectedRule is not { } rule || SelectedNode is not { } selected)
        {
            return;
        }

        var node = new ConditionNodeViewModel(kind);

        if (selected.CanAcceptChild)
        {
            selected.Add(node);
        }
        else if (selected.Parent is { } parent)
        {
            parent.Insert(parent.Children.IndexOf(selected) + 1, node);
        }
        else
        {
            // 루트가 리프면 AND로 감싸 두 조건을 나란히 둔다.
            var wrapper = selected.WrapIn(ConditionNodeKind.And);
            wrapper.Add(node);
            rule.ReplaceRoot(wrapper);
            BindTree();
        }

        rule.NotifyChanged();
        ViewModel.Refresh();
    }

    private void OnWrapAndClick(object sender, RoutedEventArgs e) => Wrap(ConditionNodeKind.And);

    private void OnWrapOrClick(object sender, RoutedEventArgs e) => Wrap(ConditionNodeKind.Or);

    /// <summary>
    /// 고른 줄을 AND/OR로 감싼다. 감싼 직후 **빈 줄 하나를 같이 넣는다.**
    /// 자식이 하나뿐인 그룹은 의미가 없어서, 트리에는 그룹이 보이는데 수식에는 안 나오는
    /// 어긋남이 생기기 때문이다.
    /// </summary>
    private void Wrap(ConditionNodeKind kind)
    {
        if (ViewModel.SelectedRule is not { } rule || SelectedNode is not { } selected)
        {
            return;
        }

        var wrapper = selected.WrapIn(kind);
        wrapper.Add(ConditionNodeViewModel.Leaf(string.Empty));

        if (wrapper.Parent is null)
        {
            rule.ReplaceRoot(wrapper);
            BindTree();
        }

        rule.NotifyChanged();
        ViewModel.Refresh();
    }

    private void OnNodeUpClick(object sender, RoutedEventArgs e) => MoveNode(up: true);

    private void OnNodeDownClick(object sender, RoutedEventArgs e) => MoveNode(up: false);

    private void MoveNode(bool up)
    {
        if (SelectedNode is not { } selected || ViewModel.SelectedRule is not { } rule)
        {
            return;
        }

        if (up ? selected.MoveUp() : selected.MoveDown())
        {
            rule.NotifyChanged();
            ViewModel.Refresh();
        }
    }

    private void OnNodeDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRule is not { } rule || SelectedNode is not { } selected)
        {
            return;
        }

        if (selected.Parent is { } parent)
        {
            parent.Remove(selected);
        }
        else
        {
            // 루트는 지우지 않고 빈 리프로 되돌린다.
            rule.ReplaceRoot(ConditionNodeViewModel.Leaf(string.Empty));
            BindTree();
        }

        rule.NotifyChanged();
        ViewModel.Refresh();
    }

    /// <summary>드래그로 옮긴 뒤에는 부모 포인터를 다시 맞춘다.</summary>
    private void OnTreeDragCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        if (ViewModel.SelectedRule is { } rule)
        {
            rule.Root.Reparent();
            rule.NotifyChanged();
            ViewModel.Refresh();
        }
    }

    // ── 도우미 ───────────────────────────────────────────────────────────

    private static T? Context<T>(object sender)
        where T : class =>
        (sender as FrameworkElement)?.DataContext as T;

    private static void Attach(object picker)
    {
        if (App.MainWindow is { } window)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }
    }

    /// <summary>
    /// 저장 실패는 사람 말로 알린다. 사용자는 YAML을 직접 쓴 적이 없으니 줄·열은 도움이 안 된다.
    /// (파일을 열다 실패한 경우는 <see cref="ShowFileParseErrorAsync"/>에서 위치를 보여준다.)
    /// </summary>
    private Task ShowParseErrorAsync(RuleParseException ex) => ShowAsync(
        UiStrings.Get("RuleMaker_SaveFailed"),
        UiStrings.Format("RuleMaker_SaveFailedBody", ex.Detail));

    /// <summary>파일을 열다 만난 오류. 이때는 줄·열이 실제로 도움이 된다.</summary>
    private Task ShowFileParseErrorAsync(RuleParseException ex) => ShowAsync(
        UiStrings.Get("RuleMaker_OpenFailed"),
        UiStrings.Format("RuleMaker_ParseErrorBody", ex.Line, ex.Column, ex.Detail));

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "닫기",
    }.ShowAsync().AsTask();
}

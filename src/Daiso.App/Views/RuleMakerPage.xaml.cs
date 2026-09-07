using Daiso.App.ViewModels;
using Daiso.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Daiso.App.Views;

public sealed partial class RuleMakerPage : Page
{
    public RuleMakerPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<RuleMakerViewModel>();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += (_, _) =>
        {
            if (ViewModel.Rules.Count == 0)
            {
                ViewModel.NewCommand.Execute(null);
            }

            BindTree();
            ViewModel.RefreshLibraryCommand.Execute(null);
            BuildRecentFlyout();
            BuildLibraryFlyout();
        };
    }

    public RuleMakerViewModel ViewModel { get; }

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

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
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
            await ShowParseErrorAsync(ex);
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
        picker.FileTypeChoices.Add("daiso 규칙", [".daiso"]);
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
            await ShowAsync("연동할 수 없음", ex.Message);
        }
    }

    // ── 지시문 마이그레이션 (REQUIREMENTS §7) ────────────────────────────

    /// <summary>좌우 diff를 보여 주고 방향을 고르게 한다. 고르기 전에는 아무것도 쓰지 않는다.</summary>
    private async void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ProjectDirectory is not { Length: > 0 } directory)
        {
            await ShowAsync("프로젝트 폴더가 필요하다", "머리말의 '프로젝트 폴더'에 경로를 넣고 다시 눌러라");
            return;
        }

        var migration = App.Services.GetRequiredService<MigrationViewModel>();

        try
        {
            migration.Inspect(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync("읽을 수 없음", ex.Message);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "CLAUDE.md ↔ AGENTS.md",
            Content = BuildMigrationView(migration),
            PrimaryButtonText = "CLAUDE.md → AGENTS.md",
            SecondaryButtonText = "AGENTS.md → CLAUDE.md",
            CloseButtonText = "닫기",
            IsPrimaryButtonEnabled = migration.CanMigrateToCodex,
            IsSecondaryButtonEnabled = migration.CanMigrateToClaude,
            DefaultButton = ContentDialogButton.Close,
        };

        var choice = await dialog.ShowAsync();

        var direction = choice switch
        {
            ContentDialogResult.Primary => (MigrationDirection?)MigrationDirection.ClaudeToCodex,
            ContentDialogResult.Secondary => MigrationDirection.CodexToClaude,
            _ => null,
        };

        if (direction is null)
        {
            return;
        }

        try
        {
            var result = migration.Apply(direction.Value);
            var target = result.Target == ToolKind.Claude ? "CLAUDE.md" : "AGENTS.md";

            await ShowAsync(
                $"{target} 를 갱신했다",
                result.Warnings.Count == 0 ? "경고 없음" : string.Join('\n', result.Warnings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowAsync("옮길 수 없음", ex.Message);
        }
    }

    private static FrameworkElement BuildMigrationView(MigrationViewModel migration)
    {
        var panel = new StackPanel { Spacing = 8, Width = 640 };

        panel.Children.Add(new TextBlock
        {
            Text = migration.Notes,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "왼쪽 CLAUDE.md, 오른쪽 AGENTS.md. daiso 마커 블록은 비교에서 뺀다",
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
            RecentFlyout.Items.Add(new MenuFlyoutItem { Text = "최근 파일 없음", IsEnabled = false });
        }
    }

    private void BuildLibraryFlyout()
    {
        LibraryFlyout.Items.Clear();

        var save = new MenuFlyoutItem { Text = "라이브러리에 저장" };
        save.Click += async (_, _) =>
        {
            try
            {
                ViewModel.SaveToLibraryCommand.Execute(null);
                BuildLibraryFlyout();
            }
            catch (RuleParseException ex)
            {
                await ShowParseErrorAsync(ex);
            }
        };

        LibraryFlyout.Items.Add(save);
        LibraryFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var path in ViewModel.LibraryPresets)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileNameWithoutExtension(path) };
            item.Click += async (_, _) => await OpenPathAsync(path);
            LibraryFlyout.Items.Add(item);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            ViewModel.Open(path);
            BindTree();
        }
        catch (RuleParseException ex)
        {
            await ShowParseErrorAsync(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync("열 수 없음", ex.Message);
        }
    }

    // ── Global 행동 ──────────────────────────────────────────────────────

    private void OnGlobalUpClick(object sender, RoutedEventArgs e) =>
        ViewModel.MoveGlobalActionUpCommand.Execute(Context<ActionEditViewModel>(sender));

    private void OnGlobalDownClick(object sender, RoutedEventArgs e) =>
        ViewModel.MoveGlobalActionDownCommand.Execute(Context<ActionEditViewModel>(sender));

    private void OnGlobalRemoveClick(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveGlobalActionCommand.Execute(Context<ActionEditViewModel>(sender));

    // ── 규칙 행동 ────────────────────────────────────────────────────────

    private void OnAddRuleActionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRule is { } rule)
        {
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

    private void OnAddLeafClick(object sender, RoutedEventArgs e) => AddNode(ConditionNodeKind.Leaf);

    private void OnAddAndClick(object sender, RoutedEventArgs e) => AddNode(ConditionNodeKind.And);

    private void OnAddOrClick(object sender, RoutedEventArgs e) => AddNode(ConditionNodeKind.Or);

    private void OnAddNotClick(object sender, RoutedEventArgs e) => AddNode(ConditionNodeKind.Not);

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

    private void OnWrapNotClick(object sender, RoutedEventArgs e) => Wrap(ConditionNodeKind.Not);

    private void Wrap(ConditionNodeKind kind)
    {
        if (ViewModel.SelectedRule is not { } rule || SelectedNode is not { } selected)
        {
            return;
        }

        var wrapper = selected.WrapIn(kind);

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

    private Task ShowParseErrorAsync(RuleParseException ex) => ShowAsync(
        "규칙을 저장할 수 없다",
        $"line {ex.Line}, column {ex.Column}\n\n{ex.Detail}");

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "닫기",
    }.ShowAsync().AsTask();
}

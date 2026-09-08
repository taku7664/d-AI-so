using Daiso.App.Strings;
using Daiso.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Daiso.App.Views;

public sealed partial class PromptsPage : Page
{
    public PromptsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<PromptsViewModel>();

        Loaded += (_, _) => ViewModel.RefreshGalleryCommand.Execute(null);
    }

    public PromptsViewModel ViewModel { get; }

    /// <summary>목록 항목의 접근성 이름을 프롬프트 이름으로.</summary>
    private void OnPromptContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is PromptGalleryItemViewModel item)
        {
            AutomationProperties.SetName(args.ItemContainer, item.Name);
        }
    }

    /// <summary>아는 프로젝트를 채운다. 폴더 선택 대화상자 대신 앱이 이미 아는 폴더에서 고른다.</summary>
    private async void OnProjectFlyoutOpening(object? sender, object e)
    {
        await ViewModel.LoadProjectChoicesAsync();

        ProjectFlyout.Items.Clear();

        foreach (var path in ViewModel.ProjectChoices)
        {
            var item = new MenuFlyoutItem { Text = path };
            item.Click += (_, _) => ViewModel.ProjectDirectory = path;
            ProjectFlyout.Items.Add(item);
        }

        if (ProjectFlyout.Items.Count == 0)
        {
            ProjectFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = UiStrings.Get("Prompts_NoKnownProjects"),
                IsEnabled = false,
            });
        }
    }

    private async void OnWriteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.WriteIntoProjectCommand.Execute(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowAsync(UiStrings.Get("Prompts_WriteFailed"), ex.Message);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not { IsMine: true } item)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Format("Prompts_ConfirmRemove", item.Name),
            Content = new TextBlock
            {
                Text = UiStrings.Get("Prompts_ConfirmRemoveBody"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = UiStrings.Get("Common_Delete"),
            CloseButtonText = UiStrings.Get("Common_Cancel"),
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveSelected();
        }
    }

    private void OnCopyBodyClick(object sender, RoutedEventArgs e) =>
        Copy(ViewModel.EditBody);

    private void OnCopyStarterClick(object sender, RoutedEventArgs e) =>
        Copy(ViewModel.StarterMessage ?? string.Empty);

    private void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.StatusText = UiStrings.Get("Prompts_Copied");
    }

    private Task ShowAsync(string title, string body) => new ContentDialog
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = UiStrings.Get("Common_Close"),
    }.ShowAsync().AsTask();
}

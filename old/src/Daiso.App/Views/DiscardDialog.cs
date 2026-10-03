using Daiso.App.Strings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Views;

/// <summary>
/// 저장하지 않은 편집이 있을 때 "버리고 넘어갈까요?"를 묻는다. (ARCHITECTURE §6.2)
/// 내 규칙·내 프롬프트가 같은 문구, 같은 버튼 순서를 쓴다.
/// </summary>
internal static class DiscardDialog
{
    /// <summary>true면 버리고 진행, false면 머무른다.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = UiStrings.Get("Common_DiscardTitle"),
            Content = new TextBlock
            {
                Text = UiStrings.Get("Common_DiscardBody"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = UiStrings.Get("Common_Discard"),
            CloseButtonText = UiStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

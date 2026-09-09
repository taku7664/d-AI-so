using Daiso.App.Strings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daiso.App.Services;

/// <summary>
/// 대화상자를 띄우는 곳. <b>누가 대화상자를 소유하는가</b>에 대한 답이다. (docs/REVIEW_BACKLOG.md D1)
///
/// <para>
/// 예전에는 페이지가 소유했다. <c>ContentDialog</c> 는 <c>XamlRoot</c> 가 있어야 뜨고 그건 페이지가 가지고 있으니,
/// "이름을 물어보고 저장" 같은 일이 전부 페이지 코드비하인드의 <c>Click</c> 핸들러여야 했다.
/// 그래서 목록 한 줄의 생김새(<c>DataTemplate</c>)가 페이지에 묶여 빠져나오지 못했다 —
/// 요약 화면이 트리 16겹으로 남은 이유가 이것이다.
/// </para>
///
/// <para>
/// 이제는 셸이 <see cref="Attach"/> 로 창의 <c>XamlRoot</c> 를 한 번 건네고, 뷰모델이 이 통로로 묻는다.
/// 뷰모델은 <c>ContentDialog</c> 를 모르고, 페이지는 대화상자를 위해 핸들러를 들고 있지 않아도 된다.
/// </para>
/// </summary>
public interface IDialogHost
{
    /// <summary>알림 하나. 닫기만 있다.</summary>
    Task NoticeAsync(string title, string body);

    /// <summary>예/아니오. 사람이 확인 단추를 눌렀으면 true.</summary>
    Task<bool> ConfirmAsync(string title, string body, string confirmText);

    /// <summary>한 줄을 받아 온다. 취소했거나 빈 줄이면 null.</summary>
    Task<string?> AskTextAsync(string title, string header, string placeholder, string confirmText);
}

/// <inheritdoc cref="IDialogHost" />
public sealed class DialogHost : IDialogHost
{
    /// <summary>입력 대화상자의 폭. 제목보다 길어야 헤더가 접히지 않는다.</summary>
    private const double InputWidth = 360;

    private XamlRoot? _root;

    /// <summary>
    /// 창이 뜬 뒤 셸이 한 번 부른다. 이걸 받기 전에는 대화상자를 띄울 수 없다 —
    /// 그때는 조용히 아무 일도 하지 않는다(앱을 죽이지 않는다).
    /// </summary>
    public void Attach(XamlRoot root) => _root = root;

    /// <inheritdoc />
    public async Task NoticeAsync(string title, string body)
    {
        if (_root is null)
        {
            return;
        }

        await Show(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = UiStrings.Get("Common_Close"),
        }).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string body, string confirmText)
    {
        if (_root is null)
        {
            return false;
        }

        var result = await Show(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = UiStrings.Get("Common_Cancel"),
        }).ConfigureAwait(true);

        return result == ContentDialogResult.Primary;
    }

    /// <inheritdoc />
    public async Task<string?> AskTextAsync(string title, string header, string placeholder, string confirmText)
    {
        if (_root is null)
        {
            return null;
        }

        var box = new TextBox { Header = header, PlaceholderText = placeholder };
        var panel = new StackPanel { Width = InputWidth };
        panel.Children.Add(box);

        var result = await Show(new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = confirmText,
            CloseButtonText = UiStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        }).ConfigureAwait(true);

        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text
            : null;
    }

    private async Task<ContentDialogResult> Show(ContentDialog dialog)
    {
        dialog.XamlRoot = _root;

        try
        {
            return await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // 이미 다른 대화상자가 떠 있거나 창이 닫히는 중이다. 한 번의 물음이 지나간 것뿐이다
            return ContentDialogResult.None;
        }
    }
}

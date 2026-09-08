using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.Controls;

/// <summary>
/// WinUI는 빈 자리를 눌러도 포커스를 옮기지 않아 입력 칸의 커서가 그대로 남는다.
/// 포커스를 받을 컨트롤이 없는 곳을 누르면 페이지 자신이 포커스를 잠깐 받아 입력 칸을 놓아 준다. (ARCHITECTURE §6.2)
/// </summary>
internal static class FocusRelease
{
    /// <summary>페이지 하나에 붙인다. 생성자에서 한 번.</summary>
    internal static void Attach(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // 자식이 처리한 눌림도 받아야 한다. 카드·목록 배경은 눌림을 처리하지 않지만 ScrollViewer는 처리할 수 있다
        page.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), handledEventsToo: true);

        // 페이지가 포커스를 잃은 뒤에야 탭 정지를 되돌린다. 포커스를 가진 채로 되돌리면 XAML이 포커스를 첫 컨트롤로 넘겨
        // (맨 위 입력칸) ScrollViewer가 그것을 보이려 맨 위로 튄다 — 설정 화면에서 토글을 누르면 스크롤이 올라가던 버그
        page.LostFocus += (_, _) =>
        {
            if (!ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), page))
            {
                page.IsTabStop = false;
            }
        };
    }

    private static void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Page page || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (TakesFocus(source, page))
        {
            return;
        }

        if (FocusManager.GetFocusedElement(page.XamlRoot) is not Control { } focused || focused == page)
        {
            return;
        }

        // 페이지는 탭 순서에 없다. 포커스를 받는 동안만 탭 정지로 만들고, 포커스가 떠나면(LostFocus) 되돌린다
        page.IsTabStop = true;
        page.Focus(FocusState.Pointer);
    }

    /// <summary>누른 자리에서 위로 올라가며 포커스를 받는 컨트롤(입력 칸·버튼·목록 항목·탭…)이 있으면 그쪽이 포커스를 가져간다.</summary>
    private static bool TakesFocus(DependencyObject source, Page page)
    {
        for (var node = source; node is not null && node != page; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Control { IsTabStop: true, IsEnabled: true } or TextBlock { IsTextSelectionEnabled: true })
            {
                return true;
            }
        }

        return false;
    }
}

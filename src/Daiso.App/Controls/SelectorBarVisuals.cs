using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Daiso.App.Controls;

/// <summary>
/// SelectorBar 항목은 누른 뒤 포인터가 항목 밖으로 나가면 눌림(회색) 상태가 남는다(ItemContainer의 상태 전환 누락).
/// 포인터가 나가거나 캡처를 잃거나 선택이 바뀌면 지금 선택 여부에 맞는 기본 상태로 되돌린다.
/// 상태 이름은 WinUI generic.xaml의 SelectorBarItem 템플릿(UnselectedNormal / SelectedNormal)을 따른다. (ARCHITECTURE §6.2)
/// </summary>
internal static class SelectorBarVisuals
{
    /// <summary>탭 띠 하나에 되돌리기를 붙인다. 페이지 생성자에서 한 번.</summary>
    internal static void ResetPressedOnLeave(SelectorBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        foreach (var item in bar.Items)
        {
            item.PointerExited += OnLeave;
            item.PointerCaptureLost += OnLeave;
            item.PointerCanceled += OnLeave;
        }

        // 선택이 바뀌면 이전·새 항목 모두 기본 상태로. 누른 항목은 포인터가 아직 위에 있어도 다음 이동에서 다시 PointerOver가 된다
        bar.SelectionChanged += (sender, _) =>
        {
            foreach (var item in sender.Items)
            {
                Settle(item);
            }
        };
    }

    private static void OnLeave(object sender, PointerRoutedEventArgs e)
    {
        if (sender is SelectorBarItem item)
        {
            Settle(item);
        }
    }

    private static void Settle(SelectorBarItem item) =>
        VisualStateManager.GoToState(item, item.IsSelected ? "SelectedNormal" : "UnselectedNormal", useTransitions: true);
}

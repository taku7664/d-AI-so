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
    /// <summary>
    /// 도구 탭을 채운다: <c>전체</c> 다음에 지금 앱이 아는 도구가 표시 순서대로.
    /// <para>
    /// <b>XAML 에 도구 이름을 적지 않는다</b> — 적어 두면 빌드된 앱에 도구를 더해도 탭에 안 나온다
    /// (docs/PLUGIN_PLAN.md Stage 3). <c>SelectorBar</c> 에는 <c>ItemsSource</c> 가 없어서 코드가 채운다.
    /// </para>
    /// <para>
    /// 순서가 곧 계약이다. 뷰모델은 탭 번호를 <c>ToolLook.DisplayOrder[번호 - 1]</c> 로 되돌리므로
    /// 여기 순서와 <see cref="Services.ToolLook.DisplayOrder"/> 가 어긋나면 탭과 도구가 어긋난다.
    /// </para>
    /// </summary>
    internal static void FillToolTabs(SelectorBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        bar.Items.Clear();
        bar.Items.Add(new SelectorBarItem
        {
            Text = Strings.UiStrings.All,
            IsSelected = true,
        });

        foreach (var kind in Services.ToolLook.DisplayOrder)
        {
            bar.Items.Add(new SelectorBarItem
            {
                Text = Services.ToolLook.Short(kind),
                // 자동화 id 는 예전 XAML 이 쓰던 이름을 그대로 지킨다. 화면 시험이 이 이름으로 누른다
                Name = "ToolTab" + kind.Id,
            });
        }

        ResetPressedOnLeave(bar);
    }

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

    /// <summary>
    /// 뷰모델이 정한 탭을 탭 띠에 반영한다. 탭 띠는 사람이 누른 것만 뷰모델로 보내는 단방향이라,
    /// 뒤로/앞으로처럼 뷰모델 쪽에서 탭이 바뀌는 경우에는 이걸로 UI를 맞춰 줘야 어긋나지 않는다.
    /// </summary>
    internal static void Select(SelectorBar bar, int index)
    {
        ArgumentNullException.ThrowIfNull(bar);

        if (index >= 0 && index < bar.Items.Count && !ReferenceEquals(bar.SelectedItem, bar.Items[index]))
        {
            bar.SelectedItem = bar.Items[index];
        }
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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.Controls;

/// <summary>
/// WinUI는 빈 자리를 눌러도 포커스를 옮기지 않아 입력 칸의 커서가 그대로 남는다.
/// 포커스를 받을 컨트롤이 없는 곳을 누르면 크기 0인 싱크 버튼이 포커스를 받아 입력 칸을 놓아 준다. (ARCHITECTURE §6.2)
///
/// 세 가지를 지켜야 스크롤이 맨 위로 튀지 않는다. 셋 다 실제로 겪은 것이다.
/// 1. 싱크는 잎(leaf) 컨트롤이어야 한다. Page를 싱크로 쓰면 ContentControl이라 포커스가 안쪽 첫 입력 칸으로 흘러내린다
///    (측정: page.Focus()는 True를 돌려주지만 ~90ms 뒤 포커스가 맨 위 TextBox로 옮겨간다).
/// 2. 싱크는 ScrollViewer 바깥, 페이지 루트 패널에 둔다. 안에 있으면 포커스가 스크롤을 끌고 다닌다.
/// 3. 누를 때와 뗄 때 둘 다 처리한다. 누를 때만 하면 WinUI가 뗄 때 빈 배경 클릭을 처리하면서 포커스를
///    ScrollViewer 안 첫 포커스 가능 요소로 밀어넣어 우리 처리를 덮어쓴다
///    (측정: 누름 88ms 뒤 sink -> SessionHomeBox, 직후 BringIntoView 로 offset 626 -> 125).
/// </summary>
internal static class FocusRelease
{
    /// <summary>페이지 하나에 붙인다. 생성자에서 한 번.</summary>
    internal static void Attach(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Button? sink = null;

        // 자식이 처리한 눌림도 받아야 한다. 카드·목록 배경은 눌림을 처리하지 않지만 ScrollViewer는 처리할 수 있다
        var handler = new PointerEventHandler((_, e) => Release(page, ref sink, e));
        page.AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true);
        page.AddHandler(UIElement.PointerReleasedEvent, handler, handledEventsToo: true);
    }

    private static void Release(Page page, ref Button? sink, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || TakesFocus(source, page))
        {
            return;
        }

        if (FocusManager.GetFocusedElement(page.XamlRoot) is not Control focused || ReferenceEquals(focused, page))
        {
            return;
        }

        sink ??= CreateSink(page);
        sink?.Focus(FocusState.Pointer);
    }

    /// <summary>페이지 루트 패널에 크기 0인 싱크를 넣는다. 루트가 패널이 아니면 놓아 줄 곳이 없다.</summary>
    private static Button? CreateSink(Page page)
    {
        if (page.Content is not Panel root)
        {
            return null;
        }

        var sink = new Button
        {
            Name = "FocusSink",
            Width = 0,
            Height = 0,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Opacity = 0,
            IsTabStop = true,
            UseSystemFocusVisuals = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        root.Children.Add(sink);
        return sink;
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

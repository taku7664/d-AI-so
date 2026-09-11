using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Daiso.App.Controls;

/// <summary>
/// "안 본 것이 있다"를 알리는 빨간 점. 점 바깥으로 고리가 퍼져 나가며 사라지는 연출을 반복한다. (ARCHITECTURE §6.2)
/// 탭·목록 항목 모서리에 붙인다. 보이는 동안만 애니메이션이 돈다(Loaded/Unloaded).
/// </summary>
public sealed class PulseDot : Grid
{
    private readonly Storyboard _pulse = new() { RepeatBehavior = RepeatBehavior.Forever };

    public PulseDot()
    {
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;

        var ring = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = (Brush)Application.Current.Resources["PulseDotBrush"],
            Opacity = 0,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
        };
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            // 테두리가 카드 바탕색이라 테마를 탄다. 스타일로 입혀야 다크에서 따라온다
            Style = (Style)Application.Current.Resources["PulseDotFace"],
        };

        Children.Add(ring);
        Children.Add(dot);

        var duration = new Duration(TimeSpan.FromMilliseconds(1400));
        _pulse.Children.Add(Animate(ring, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1, 2.4, duration));
        _pulse.Children.Add(Animate(ring, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1, 2.4, duration));
        _pulse.Children.Add(Animate(ring, "Opacity", 0.7, 0, duration));

        Loaded += (_, _) => _pulse.Begin();
        Unloaded += (_, _) => _pulse.Stop();
    }

    private static DoubleAnimation Animate(DependencyObject target, string path, double from, double to, Duration duration)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = duration, EnableDependentAnimation = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        return animation;
    }
}

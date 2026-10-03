using Daiso.App.Services;
using Daiso.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.Controls;

/// <summary>
/// 도구 로고를 원 안에 그린 아이콘. 글자 배지 대신 제작사 로고(simple-icons, CC0)를 쓴다. (ARCHITECTURE §6.2)
/// 원 색은 <see cref="ToolLook.Brush"/>, 로고는 <see cref="ToolLook.LogoPath"/>. 크기는 <see cref="Size"/> 하나로 정한다.
/// </summary>
public sealed class ToolIcon : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _circle = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _logo = new() { Fill = new SolidColorBrush(Colors.White), Stretch = Stretch.Uniform };
    private readonly Viewbox _box = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public ToolIcon()
    {
        _box.Child = _logo;
        Children.Add(_circle);
        Children.Add(_box);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Apply();
    }

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(ToolKind), typeof(ToolIcon), new PropertyMetadata(ToolKind.Claude, (d, _) => ((ToolIcon)d).Apply()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(ToolIcon), new PropertyMetadata(24d, (d, _) => ((ToolIcon)d).Apply()));

    /// <summary>어느 도구의 로고인가.</summary>
    public ToolKind Kind
    {
        get => (ToolKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>원의 지름(px). 로고는 그 안에 60%로 들어간다.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Apply()
    {
        var size = Math.Max(8, Size);
        Width = size;
        Height = size;
        _circle.Width = size;
        _circle.Height = size;
        _circle.Fill = ToolLook.Brush(Kind);
        _box.Width = size * 0.6;
        _box.Height = size * 0.6;
        _logo.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), ToolLook.LogoPath(Kind));
        ToolTipService.SetToolTip(this, ToolLook.Title(Kind));
    }
}

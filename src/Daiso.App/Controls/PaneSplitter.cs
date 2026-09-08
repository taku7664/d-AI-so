using Daiso.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Daiso.App.Controls;

/// <summary>
/// 두 판 사이에 놓는 끌 수 있는 구분선. (ARCHITECTURE §6.2)
/// 자기 왼쪽 열(<c>Grid.Column - 1</c>)의 너비를 끌어서 바꾸고, 그 열의 MinWidth·MaxWidth 안에서만 움직인다.
/// <see cref="SettingsKey"/>를 주면 놓을 때 너비를 설정에 저장하고 다음에 열 때 되살린다.
/// 포인터 이벤트로 직접 처리한다. 마우스·터치·펜 모두 같은 길을 탄다.
/// </summary>
public sealed class PaneSplitter : Grid
{
    private const double HitWidth = 12;
    private const double LineWidth = 2;

    private readonly Border _line;
    private Grid? _owner;
    private ColumnDefinition? _column;
    private double _startWidth;
    private double _startX;
    private bool _dragging;

    public PaneSplitter()
    {
        Width = HitWidth;
        Background = new SolidColorBrush(Colors.Transparent);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Stretch;

        _line = new Border
        {
            Width = LineWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(1),
            Background = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
        };
        Children.Add(_line);

        Loaded += OnLoaded;
        PointerEntered += (_, _) => Highlight(true);
        PointerExited += (_, _) => { if (!_dragging) { Highlight(false); } };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerReleased;
        PointerCaptureLost += OnPointerReleased;

        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }

    /// <summary>설정에 너비를 저장할 때 쓰는 이름. 비우면 저장하지 않는다.</summary>
    public string? SettingsKey { get; set; }

    private ColumnDefinition? TargetColumn
    {
        get
        {
            if (_column is not null)
            {
                return _column;
            }

            _owner = Parent as Grid;
            var index = GetColumn(this) - 1;

            if (_owner is null || index < 0 || index >= _owner.ColumnDefinitions.Count)
            {
                return null;
            }

            _column = _owner.ColumnDefinitions[index];
            return _column;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TargetColumn is not { } column || SettingsKey is not { Length: > 0 } key)
        {
            return;
        }

        if (App.Services.GetRequiredService<ISettingsStore>().Current.PaneWidths.TryGetValue(key, out var saved))
        {
            column.Width = new GridLength(Clamp(column, saved));
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (TargetColumn is not { } column || _owner is null)
        {
            return;
        }

        _dragging = CapturePointer(e.Pointer);
        _startWidth = column.ActualWidth;
        _startX = e.GetCurrentPoint(_owner).Position.X;
        Highlight(true);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || TargetColumn is not { } column || _owner is null)
        {
            return;
        }

        var x = e.GetCurrentPoint(_owner).Position.X;
        column.Width = new GridLength(Clamp(column, _startWidth + (x - _startX)));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCaptures();
        Highlight(false);

        if (TargetColumn is { } column && SettingsKey is { Length: > 0 } key)
        {
            var settings = App.Services.GetRequiredService<ISettingsStore>();
            settings.Current.PaneWidths[key] = (int)Math.Round(column.ActualWidth);
            settings.Save();
        }
    }

    private static double Clamp(ColumnDefinition column, double width)
    {
        var max = double.IsInfinity(column.MaxWidth) ? double.MaxValue : column.MaxWidth;
        return Math.Clamp(width, column.MinWidth, max);
    }

    private void Highlight(bool on) =>
        _line.Background = (Brush)Application.Current.Resources[on ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush"];
}

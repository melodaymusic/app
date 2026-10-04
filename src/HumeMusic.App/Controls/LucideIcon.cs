using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HumeMusic.Controls;

/// <summary>
/// Renders a Lucide icon natively as a stroked XAML Path inside a 24x24 Viewbox.
/// Unless a Foreground is set directly on the icon, its colour follows the nearest hosting
/// ContentPresenter (the content area of a Button, ToggleButton, ListViewItem...), so the host's
/// normal / hover / pressed / checked / disabled visual states always apply to the icon.
/// </summary>
public sealed partial class LucideIcon : UserControl
{
    private readonly Microsoft.UI.Xaml.Shapes.Path _path;

    public LucideIcon()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        _path = new Microsoft.UI.Xaml.Shapes.Path
        {
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeThickness = 2,
        };
        BindColor(this);
        Loaded += (_, _) => AttachToHost();

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_path);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        Width = 20;
        Height = 20;
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(LucideIcon), new PropertyMetadata(null, (d, _) => ((LucideIcon)d).UpdateGeometry()));

    /// <summary>Lucide icon name in PascalCase, e.g. "Play", "SkipForward", "ListMusic".</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool), typeof(LucideIcon), new PropertyMetadata(false, (d, _) => ((LucideIcon)d).UpdateFill()));

    /// <summary>Also fills the shape with the foreground colour (used for solid play/pause glyphs).</summary>
    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(LucideIcon), new PropertyMetadata(2.0, (d, e) => ((LucideIcon)d)._path.StrokeThickness = (double)e.NewValue));

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private DependencyObject _colorSource = null!;

    private void BindColor(DependencyObject source)
    {
        _colorSource = source;
        var path = new PropertyPath("Foreground");
        _path.SetBinding(Shape.StrokeProperty, new Binding { Source = source, Path = path });
        if (IsFilled) _path.SetBinding(Shape.FillProperty, new Binding { Source = source, Path = path });
    }

    private void AttachToHost()
    {
        if (ReadLocalValue(ForegroundProperty) != DependencyProperty.UnsetValue) return;
        for (var p = VisualTreeHelper.GetParent(this); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ContentPresenter presenter)
            {
                if (!ReferenceEquals(_colorSource, presenter)) BindColor(presenter);
                return;
            }
            if (p is Page or UserControl and not LucideIcon) break;
        }
    }

    private void UpdateGeometry()
    {
        if (Icon is { } name && LucideIconData.Paths.TryGetValue(name, out var data))
        {
            _path.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
        }
        else
        {
            _path.Data = null;
        }
    }

    private void UpdateFill()
    {
        if (IsFilled)
            _path.SetBinding(Shape.FillProperty, new Binding { Source = _colorSource ?? this, Path = new PropertyPath("Foreground") });
        else
            _path.ClearValue(Shape.FillProperty);
    }
}

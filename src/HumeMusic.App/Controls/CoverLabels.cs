using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>
/// The one typography of Meloday Music's own covers (Meloday's, and every station's): a name set
/// right-aligned in the top right and a label ("Meloday", "Radio") in the bottom left, laid out in a
/// 1000-unit square. Everything that draws such a cover, live (<see cref="MelodayArt"/>,
/// <see cref="CoverLabels"/>) or as a picture (<c>MelodayCover</c>, <c>StaticCover</c>), uses these numbers.
/// </summary>
public static class CoverType
{
    public const double Unit = 1000;
    /// <summary>The name's box: 700 wide, 100 down from the top, 110 in from the right; up to four lines of 92.</summary>
    public const double NameWidth = 700, NameTop = 100, NameRight = 110, NameSize = 92, NameLine = 92, NameBaseline = 74;
    public const int NameLines = 4;
    /// <summary>The label: 110 in from the left, 100 up from the bottom, size 87.</summary>
    public const double LabelLeft = 110, LabelBottom = 100, LabelSize = 87;
}

/// <summary>
/// A name (top right) and a label (bottom left) over a cover, scaled to the cover's size: the layout
/// of <see cref="CoverType"/> for a card that draws its own background (a station's colour field).
/// </summary>
public sealed partial class CoverLabels : Grid
{
    private readonly TextBlock _name = new()
    {
        Width = CoverType.NameWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, CoverType.NameTop, CoverType.NameRight, 0),
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.WrapWholeWords,
        MaxLines = CoverType.NameLines,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FontSize = CoverType.NameSize,
        LineHeight = CoverType.NameLine,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.ExtraBold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
    };

    private readonly TextBlock _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(CoverType.LabelLeft, 0, 0, CoverType.LabelBottom),
        FontSize = CoverType.LabelSize,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
    };

    public CoverLabels()
    {
        IsHitTestVisible = false;
        var font = (FontFamily)Application.Current.Resources["HumeFont"];
        _name.FontFamily = font;
        _label.FontFamily = font;
        var canvas = new Grid { Width = CoverType.Unit, Height = CoverType.Unit };
        canvas.Children.Add(_name);
        canvas.Children.Add(_label);
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = canvas });
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._name.Text = e.NewValue as string ?? string.Empty));

    /// <summary>The name, top right.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._label.Text = e.NewValue as string ?? string.Empty));

    /// <summary>The label, bottom left ("Radio").</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}

using HumeMusic.Core.Covers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>
/// The one typography of Meloday Music's own covers (Meloday's, every station's, every mix's): a name set right-aligned in the
/// top right and a label ("Meloday", "Radio", or a mix's name) in the bottom left, laid out in a 1000-unit square. Each has a
/// box <see cref="BoxWidth"/> wide at a fixed place, and a fixed size; a text too long for its box is set smaller, just enough
/// to fit it (see <see cref="CoverFit"/>). Everything that draws such a cover, live (<see cref="MelodayArt"/>,
/// <see cref="CoverLabels"/>, <see cref="MixCover"/>) or as a picture (<c>MelodayCover</c>, <c>StaticCover</c>), uses these numbers.
/// </summary>
public static class CoverType
{
    public const double Unit = 1000;
    /// <summary>Both boxes are 760 wide.</summary>
    public const double BoxWidth = 760;
    /// <summary>The name's box: 100 down from the top, its right edge 110 in from the right; up to four lines of 92.</summary>
    public const double NameTop = 100, NameRight = 110, NameSize = 92, NameLine = 92, NameBaseline = 74;
    public const int NameLines = 4;
    /// <summary>The label's box: 110 in from the left, its bottom edge 100 up from the bottom; up to four lines of 87 (lines 100 apart).</summary>
    public const double LabelLeft = 110, LabelBottom = 100, LabelSize = 87, LabelLine = 100, LabelBaseline = 80;
    public const int LabelLines = 4;
}

/// <summary>
/// The name and label of a cover, laid out in the 1000-unit square of <see cref="CoverType"/> (not scaled: it is put in a
/// <see cref="Viewbox"/> or on a canvas that is). Each text is set at its usual size, or smaller when it wouldn't fit its box.
/// </summary>
public sealed class CoverTextLayer : Grid
{
    private readonly TextBlock _name = new()
    {
        Width = CoverType.BoxWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, CoverType.NameTop, CoverType.NameRight, 0),
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.ExtraBold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        IsHitTestVisible = false,
    };

    private readonly TextBlock _label = new()
    {
        Width = CoverType.BoxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(CoverType.LabelLeft, 0, 0, CoverType.LabelBottom),
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        IsHitTestVisible = false,
    };

    // One word at a time, to see that none is wider than the box (a long word would be broken across lines).
    private readonly TextBlock _probe = new() { TextWrapping = TextWrapping.NoWrap };

    public CoverTextLayer()
    {
        Width = Height = CoverType.Unit;
        IsHitTestVisible = false;
        var font = (FontFamily)Application.Current.Resources["HumeFont"];
        _name.FontFamily = _label.FontFamily = _probe.FontFamily = font;
        Children.Add(_name);
        Children.Add(_label);
        SetSize(_name, CoverType.NameSize, CoverType.NameLine);
        SetSize(_label, CoverType.LabelSize, CoverType.LabelLine);
    }

    /// <summary>The name, top right.</summary>
    public string Title
    {
        get => _name.Text;
        set => Fit(_name, value, CoverType.NameSize, CoverType.NameLine / CoverType.NameSize, CoverType.NameLines, FontWeights.ExtraBold);
    }

    /// <summary>The label, bottom left.</summary>
    public string Label
    {
        get => _label.Text;
        set => Fit(_label, value, CoverType.LabelSize, CoverType.LabelLine / CoverType.LabelSize, CoverType.LabelLines, FontWeights.Bold);
    }

    private static void SetSize(TextBlock block, double size, double line)
    {
        block.FontSize = size;
        block.LineHeight = line;
    }

    private void Fit(TextBlock block, string? text, double usual, double lineRatio, int maxLines, Windows.UI.Text.FontWeight weight)
    {
        block.Text = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(block.Text))
        {
            SetSize(block, usual, usual * lineRatio);
            return;
        }
        _probe.FontWeight = weight;
        var words = block.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var size = CoverFit.Size(usual, s => Fits(block, words, s, lineRatio, maxLines));
        SetSize(block, size, size * lineRatio);
    }

    private bool Fits(TextBlock block, string[] words, double size, double lineRatio, int maxLines)
    {
        SetSize(block, size, size * lineRatio);
        block.Measure(new Windows.Foundation.Size(CoverType.BoxWidth, double.PositiveInfinity));
        if (Math.Round(block.DesiredSize.Height / (size * lineRatio)) > maxLines) return false;
        _probe.FontSize = size;
        foreach (var word in words)
        {
            _probe.Text = word;
            _probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (_probe.DesiredSize.Width > CoverType.BoxWidth + 0.5) return false;
        }
        return true;
    }
}

/// <summary>
/// A name (top right) and a label (bottom left) over a cover, scaled to the cover's size: the layout
/// of <see cref="CoverType"/> for a card that draws its own background (a station's colour field, a mix's circles).
/// </summary>
public sealed partial class CoverLabels : Grid
{
    private readonly CoverTextLayer _text = new();

    public CoverLabels()
    {
        IsHitTestVisible = false;
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _text });
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Title = e.NewValue as string ?? string.Empty));

    /// <summary>The name, top right.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Label = e.NewValue as string ?? string.Empty));

    /// <summary>The label, bottom left ("Radio", or a mix's name).</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}

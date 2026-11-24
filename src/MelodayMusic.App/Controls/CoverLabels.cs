using MelodayMusic.Core.Covers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

/// <summary>
/// Every generated cover, Meloday's included, on a 1000-unit square: the playlist label ("Meloday", "Essentials", "Mix", "Genre")
/// top right in SemiBold, and the name bottom left in Bold. Both shrink to fit, to no less than 40%.
/// </summary>
public static class CoverType
{
    public const double Unit = 1000;
    public const double BoxHeight = 405;
    // The label mirrors the Meloday mark: in from the right edge as far as the mark is from the left, its capitals centred on the mark.
    // DM Sans capitals are 0.7 em tall; the baseline sits 0.7625 em down the line (the font's ascent share, where XAML puts it too).
    public const double LabelSize = 76, LabelLine = 76, LabelBaseline = 76 * 0.7625, CapHeight = 0.7;
    public const double LabelRight = MelodayMusic.Services.CoverSymbol.Left, LabelWidth = 675;
    public const double LabelTop = MelodayMusic.Services.CoverSymbol.Top + MelodayMusic.Services.CoverSymbol.Height / 2 - (LabelBaseline - CapHeight * LabelSize / 2);
    public const int LabelLines = 4;
    public const double NameWidth = 760, NameLeft = 110, NameBottom = 100, NameSize = 85, NameLine = 85, NameBaseline = 85 * 0.8;
    public const int NameLines = 3;
    public static readonly Windows.UI.Text.FontWeight LabelWeight = FontWeights.SemiBold;
    public static readonly Windows.UI.Text.FontWeight NameWeight = FontWeights.Bold;
}

public sealed class CoverTextLayer : Grid
{
    private readonly TextBlock _label = new()
    {
        Width = CoverType.LabelWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = CoverType.LabelWeight,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        IsHitTestVisible = false,
    };

    private readonly TextBlock _name = new()
    {
        Width = CoverType.NameWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = CoverType.NameWeight,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        IsHitTestVisible = false,
    };

    private readonly TextBlock _probe = new() { TextWrapping = TextWrapping.NoWrap };

    public CoverTextLayer()
    {
        Width = Height = CoverType.Unit;
        IsHitTestVisible = false;
        var font = (FontFamily)Application.Current.Resources["MelodayFont"];
        _name.FontFamily = _label.FontFamily = _probe.FontFamily = font;
        Children.Add(new Grid
        {
            Width = CoverType.LabelWidth, Height = CoverType.BoxHeight,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, CoverType.LabelTop, CoverType.LabelRight, 0), Children = { _label },
        });
        Children.Add(new Grid
        {
            Width = CoverType.NameWidth, Height = CoverType.BoxHeight,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(CoverType.NameLeft, 0, 0, CoverType.NameBottom), Children = { _name },
        });
        SetSize(_label, CoverType.LabelSize, CoverType.LabelLine);
        SetSize(_name, CoverType.NameSize, CoverType.NameLine);
    }

    /// <summary>The playlist label, top right.</summary>
    public string Label
    {
        get => _label.Text;
        set => Fit(_label, value, CoverType.LabelSize, CoverType.LabelLine / CoverType.LabelSize, CoverType.LabelLines, CoverType.LabelWeight);
    }

    /// <summary>The name, bottom left.</summary>
    public string Title
    {
        get => _name.Text;
        set => Fit(_name, value, CoverType.NameSize, CoverType.NameLine / CoverType.NameSize, CoverType.NameLines, CoverType.NameWeight);
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
        block.Measure(new Windows.Foundation.Size(block.Width, double.PositiveInfinity));
        if (block.DesiredSize.Height > CoverType.BoxHeight || Math.Round(block.DesiredSize.Height / (size * lineRatio)) > maxLines) return false;
        _probe.FontSize = size;
        foreach (var word in words)
        {
            _probe.Text = word;
            _probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (_probe.DesiredSize.Width > block.Width + 0.5) return false;
        }
        return true;
    }
}

public sealed partial class CoverLabels : Grid
{
    private readonly CoverTextLayer _text = new();

    public CoverLabels()
    {
        IsHitTestVisible = false;
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = _text });
    }

    /// <summary>The name, bottom left.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Title = e.NewValue as string ?? string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The playlist label, top right.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Label = e.NewValue as string ?? string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}

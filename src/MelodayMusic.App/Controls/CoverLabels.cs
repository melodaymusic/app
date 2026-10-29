using MelodayMusic.Core.Covers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public static class CoverType
{
    public const double Unit = 1000;
    public const double BoxWidth = 760;
    public const double NameWidth = 675;
    public const double BoxHeight = 405;
    public const double NameTop = 100, NameRight = 110, NameSize = 100, NameLine = 100, NameBaseline = 80;
    public const int NameLines = 4;
    public const double LabelLeft = 110, LabelBottom = 100, LabelSize = 88, LabelLine = 96, LabelBaseline = 81;
    public const int LabelLines = 3;
}

public sealed class CoverTextLayer : Grid
{
    private readonly TextBlock _name = new()
    {
        Width = CoverType.NameWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.Black,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        IsHitTestVisible = false,
    };

    private readonly TextBlock _label = new()
    {
        Width = CoverType.BoxWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.WrapWholeWords,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.Bold,
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
            Width = CoverType.NameWidth, Height = CoverType.BoxHeight,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, CoverType.NameTop, CoverType.NameRight, 0), Children = { _name },
        });
        Children.Add(new Grid
        {
            Width = CoverType.BoxWidth, Height = CoverType.BoxHeight,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(CoverType.LabelLeft, 0, 0, CoverType.LabelBottom), Children = { _label },
        });
        SetSize(_name, CoverType.NameSize, CoverType.NameLine);
        SetSize(_label, CoverType.LabelSize, CoverType.LabelLine);
    }

    public string Title
    {
        get => _name.Text;
        set => Fit(_name, value, CoverType.NameSize, CoverType.NameLine / CoverType.NameSize, CoverType.NameLines, FontWeights.Black);
    }

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

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Title = e.NewValue as string ?? string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CoverLabels), new PropertyMetadata(string.Empty, (d, e) => ((CoverLabels)d)._text.Label = e.NewValue as string ?? string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}

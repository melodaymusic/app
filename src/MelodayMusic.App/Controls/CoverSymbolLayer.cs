using MelodayMusic.Core.Covers;
using MelodayMusic.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class CoverSymbolLayer : Grid
{
    private readonly MelodayMark _symbol = new()
    {
        Width = CoverSymbol.Width,
        Height = CoverSymbol.Height,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(CoverSymbol.Left, CoverSymbol.Top, 0, 0),
    };

    private readonly TextBlock _badge = new()
    {
        FontSize = CoverSymbol.BadgeSize,
        FontWeight = Microsoft.UI.Text.FontWeights.Black,
        TextLineBounds = TextLineBounds.Tight,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        Visibility = Visibility.Collapsed,
    };

    public CoverSymbolLayer()
    {
        IsHitTestVisible = false;
        _badge.FontFamily = (FontFamily)Application.Current.Resources["MelodayFont"];
        var badgeRow = new Grid
        {
            Height = CoverSymbol.Height,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(CoverSymbol.Left + CoverSymbol.Width + CoverSymbol.BadgeGap, CoverSymbol.Top, 0, 0),
            Children = { _badge },
        };
        Children.Add(new Viewbox
        {
            Stretch = Stretch.UniformToFill,
            Child = new Grid { Width = 1000, Height = 1000, Children = { _symbol, badgeRow } },
        });
    }

    public string Badge
    {
        get => _badge.Text;
        set
        {
            _badge.Text = value;
            _badge.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Raised with the cover's accent whenever it changes, so the edge bars can follow it.</summary>
    internal event Action<uint>? AccentChanged;

    /// <summary>Sets the ground the mark sits on; the mark takes the cover's accent, the edge bars' colour.</summary>
    internal void SetGround(uint rgb) => SetColour(CoverEdge.Colour(rgb));

    internal void SetColour(uint ink)
    {
        var brush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(ink >> 16), (byte)(ink >> 8), (byte)ink));
        _symbol.Foreground = brush;
        _badge.Foreground = brush;
        AccentChanged?.Invoke(ink);
    }

    /// <summary>A station's mark, badge and edge bars take the colour of its moving wave.</summary>
    internal void SetStation(MelodayMusic.Core.Models.Station station) =>
        SetColour(MelodayMusic.Core.Radio.StationWave.Highlight(StationColours.Ground(station)));
}

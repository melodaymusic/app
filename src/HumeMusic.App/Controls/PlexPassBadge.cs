using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// A small gold "PLEX PASS" tag on features that rely on Plex Pass. It is only a quiet label when
/// the listener has Plex Pass, and says what's needed when they don't.
/// </summary>
public sealed partial class PlexPassBadge : UserControl
{
    // Plex's own gold, a touch deeper on light backgrounds so it stays readable.
    private static readonly Color GoldOnDark = Color.FromArgb(255, 0xE5, 0xA0, 0x0D);
    private static readonly Color GoldOnLight = Color.FromArgb(255, 0x9A, 0x6A, 0x00);

    private readonly Border _pill;
    private readonly TextBlock _text;

    public PlexPassBadge()
    {
        IsTabStop = false;
        _text = new TextBlock
        {
            Text = "PLEX PASS",
            // Small caps label: at the badge size, never below the 11px floor for anything read.
            FontSize = (double)Application.Current.Resources["HumeFontBadge"],
            FontWeight = FontWeights.Bold,
            CharacterSpacing = 60,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pill = new Border
        {
            Padding = new Thickness(6, 1, 6, 2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = _text,
        };
        Content = _pill;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Loaded += (_, _) => Apply();
        ActualThemeChanged += (_, _) => Apply();
    }

    /// <summary>On artwork or colour (always light text): the dark-background gold whatever the theme.</summary>
    public bool OnArtwork { get; set; }

    private void Apply()
    {
        var gold = OnArtwork || ActualTheme == ElementTheme.Dark ? GoldOnDark : GoldOnLight;
        _text.Foreground = new SolidColorBrush(gold);
        _pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, gold.R, gold.G, gold.B));
        _pill.Background = new SolidColorBrush(OnArtwork ? Color.FromArgb(0x99, 0x14, 0x12, 0x10) : Color.FromArgb(0x1F, gold.R, gold.G, gold.B));
        var available = App.Services.GetRequiredService<PlexPass>().IsAvailable;
        var tip = available ? "Part of your Plex Pass" : "Needs Plex Pass";
        ToolTipService.SetToolTip(this, tip);
        AutomationProperties.SetName(this, tip);
    }
}

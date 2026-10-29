using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class PlexPassBadge : UserControl
{
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
            FontSize = (double)Application.Current.Resources["MelodayFontBadge"],
            FontWeight = FontWeights.Bold,
            CharacterSpacing = 60,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pill = new Border
        {
            Padding = new Thickness(6, 1, 6, 2),
            CornerRadius = (CornerRadius)Application.Current.Resources["MelodayRadiusBadge"],
            MinHeight = (double)Application.Current.Resources["MelodayBadgeHeight"],
            BorderThickness = new Thickness(1),
            Child = _text,
        };
        Content = _pill;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Loaded += (_, _) => Apply();
        ActualThemeChanged += (_, _) => Apply();
    }

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

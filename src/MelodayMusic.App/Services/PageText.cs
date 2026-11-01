using MelodayMusic.Core.Artwork;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Services;

public static class PageText
{
    private static readonly List<(bool Dark, bool Secondary, SolidColorBrush Brush, Color Default)> Installed = [];

    public static void Install(ResourceDictionary design)
    {
        Installed.Clear();
        if (!design.ThemeDictionaries.TryGetValue("Dark", out var value) || value is not ResourceDictionary dictionary) return;
        foreach (var (key, secondary) in new[] { ("MelodayTextSecondaryBrush", true), ("MelodayTextTertiaryBrush", false) })
        {
            if (dictionary.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
                Installed.Add((true, secondary, brush, brush.Color));
        }
    }

    public static void Apply(uint? fill)
    {
        foreach (var (dark, secondary, brush, original) in Installed)
        {
            if (fill is not { } rgb)
            {
                brush.Color = original;
                continue;
            }
            var page = DominantColor.ForPageFill(rgb, darkText: !dark);
            var tint = DominantColor.TintedText(page, light: dark, secondary ? 4.5 : 3.0);
            brush.Color = Color.FromArgb(255, (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint);
        }
    }
}

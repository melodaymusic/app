using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Helpers;

/// <summary>Stable, muted colours for genre / mood / style tiles, derived from the tag name.</summary>
public static class TagColors
{
    private static readonly Color[] Palette =
    [
        ColorHelper.FromArgb(255, 0x3B, 0x4A, 0x6B),
        ColorHelper.FromArgb(255, 0x5A, 0x3E, 0x5D),
        ColorHelper.FromArgb(255, 0x2F, 0x5D, 0x50),
        ColorHelper.FromArgb(255, 0x6B, 0x4F, 0x2A),
        ColorHelper.FromArgb(255, 0x4A, 0x3F, 0x6B),
        ColorHelper.FromArgb(255, 0x6B, 0x3A, 0x3A),
        ColorHelper.FromArgb(255, 0x2E, 0x55, 0x66),
        ColorHelper.FromArgb(255, 0x54, 0x61, 0x2F),
        ColorHelper.FromArgb(255, 0x63, 0x44, 0x55),
        ColorHelper.FromArgb(255, 0x3D, 0x5A, 0x3D),
    ];

    private static readonly Dictionary<int, SolidColorBrush> Brushes = [];

    private static int IndexOf(string? name)
    {
        var hash = 17;
        foreach (var ch in name ?? string.Empty) hash = unchecked(hash * 31 + char.ToLowerInvariant(ch));
        return (int)((uint)hash % Palette.Length);
    }

    /// <summary>The colour of a name's tag, which (unlike its brush) can be read from any thread.</summary>
    public static Windows.UI.Color ColorFor(string? name) => Palette[IndexOf(name)];

    public static SolidColorBrush For(string? name)
    {
        var index = IndexOf(name);
        if (!Brushes.TryGetValue(index, out var brush))
        {
            brush = new SolidColorBrush(Palette[index]);
            Brushes[index] = brush;
        }
        return brush;
    }
}

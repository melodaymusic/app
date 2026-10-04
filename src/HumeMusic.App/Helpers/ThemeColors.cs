using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Helpers;

/// <summary>
/// Reads a design-token colour for a specific element theme (Application resources resolve for the
/// application theme, which can differ from the theme the user picked in Settings).
/// </summary>
public static class ThemeColors
{
    public static Color Get(string key, ElementTheme theme, Color fallback)
    {
        var themeKey = theme == ElementTheme.Light ? "Light" : "Dark";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var d) &&
                d is ResourceDictionary themed && themed.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                return brush.Color;
        }
        return fallback;
    }
}

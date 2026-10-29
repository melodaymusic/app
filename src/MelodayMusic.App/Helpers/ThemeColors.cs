using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Helpers;

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

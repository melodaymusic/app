using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Helpers;

public static class EditorChrome
{
    public static readonly DependencyProperty NeutralProperty = DependencyProperty.RegisterAttached(
        "Neutral", typeof(bool), typeof(EditorChrome), new PropertyMetadata(false, Changed));
    public static bool GetNeutral(DependencyObject item) => (bool)item.GetValue(NeutralProperty);
    public static void SetNeutral(DependencyObject item, bool value) => item.SetValue(NeutralProperty, value);

    private static void Changed(DependencyObject item, DependencyPropertyChangedEventArgs args)
    {
        if (item is not FrameworkElement editor || args.NewValue is not true) return;
        Apply(editor);
    }

    private static void Apply(FrameworkElement editor)
    {
        var edge = new SolidColorBrush(ColorHelper.FromArgb(0x38, 255, 255, 255));
        var hover = new SolidColorBrush(ColorHelper.FromArgb(0x70, 255, 255, 255));
        var focus = new SolidColorBrush(ColorHelper.FromArgb(255, 244, 242, 238));
        editor.Resources["TextControlBorderThemeThickness"] = new Thickness(1);
        editor.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        editor.Resources["TextControlBorderBrush"] = edge;
        editor.Resources["TextControlBorderBrushPointerOver"] = hover;
        editor.Resources["TextControlBorderBrushFocused"] = focus;
        editor.Resources["TextControlElevationBorderBrush"] = edge;
        editor.Resources["TextControlElevationBorderFocusedBrush"] = focus;
    }
}

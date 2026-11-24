using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Helpers;

/// <summary>
/// Brings controls whose templates hard-code their size in line with the shared field style:
/// the date picker gets the standard field height and text inset, and an expander's header sits
/// flush with the field labels above it, like a disclosure row.
/// </summary>
public static class FieldChrome
{
    public static readonly DependencyProperty FitProperty = DependencyProperty.RegisterAttached(
        "Fit", typeof(bool), typeof(FieldChrome), new PropertyMetadata(false, Changed));
    public static bool GetFit(DependencyObject item) => (bool)item.GetValue(FitProperty);
    public static void SetFit(DependencyObject item, bool value) => item.SetValue(FitProperty, value);

    private static void Changed(DependencyObject item, DependencyPropertyChangedEventArgs args)
    {
        if (item is not Control control || args.NewValue is not true) return;
        control.Loaded += (_, _) => Apply(control);
    }

    private static void Apply(Control control)
    {
        var resources = Application.Current.Resources;
        var height = (double)resources["MelodayControlHeight"];
        var inset = ((Thickness)resources["MelodayTextBoxPadding"]).Left;
        switch (control)
        {
            case CalendarDatePicker:
                if (Find<Border>(control, "Background") is { } field) field.MinHeight = height;
                if (Find<TextBlock>(control, "DateText") is { } text) text.Padding = new Thickness(inset, 0, 0, 0);
                break;
            case Expander:
                if (Find<ToggleButton>(control, "ExpanderHeader") is { } header)
                {
                    header.Padding = new Thickness(0);
                    header.MinHeight = height;
                }
                break;
        }
    }

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name) return match;
            if (Find<T>(child, name) is { } deeper) return deeper;
        }
        return null;
    }
}

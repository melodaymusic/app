using Microsoft.UI.Xaml;

namespace MelodayMusic.Helpers;

public static class SelectionChrome
{
    public static readonly DependencyProperty NeutralProperty = DependencyProperty.RegisterAttached(
        "Neutral", typeof(bool), typeof(SelectionChrome), new PropertyMetadata(false, (item, args) =>
        {
            if (item is FrameworkElement element && args.NewValue is true)
                element.Resources["ListViewItemSelectionIndicatorVisualEnabled"] = false;
        }));
    public static bool GetNeutral(DependencyObject item) => (bool)item.GetValue(NeutralProperty);
    public static void SetNeutral(DependencyObject item, bool value) => item.SetValue(NeutralProperty, value);
}

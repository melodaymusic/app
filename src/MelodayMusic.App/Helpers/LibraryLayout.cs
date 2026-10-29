using MelodayMusic.Controls;
using Microsoft.UI.Xaml;

namespace MelodayMusic.Helpers;

public static class LibraryLayout
{
    public static void Attach(LayoutToggle toggle, UIElement grid, UIElement list)
    {
        toggle.Load();
        void Apply()
        {
            grid.Visibility = toggle.IsList ? Visibility.Collapsed : Visibility.Visible;
            list.Visibility = toggle.IsList ? Visibility.Visible : Visibility.Collapsed;
        }
        Apply();
        toggle.Changed += (_, _) => Apply();
    }
}

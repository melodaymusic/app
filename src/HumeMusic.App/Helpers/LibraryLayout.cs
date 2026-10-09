using HumeMusic.Controls;
using Microsoft.UI.Xaml;

namespace HumeMusic.Helpers;

/// <summary>Ties a library page's grid and list to its <see cref="LayoutToggle"/>: the chosen one shows, the other doesn't.</summary>
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

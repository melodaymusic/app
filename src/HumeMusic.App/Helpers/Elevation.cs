using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Helpers;

/// <summary>Soft native drop shadows (ThemeShadow) for artwork that should lift off the page.</summary>
public static class Elevation
{
    /// <param name="element">The element casting the shadow.</param>
    /// <param name="receiver">An element behind it (not an ancestor) that the shadow falls on.</param>
    /// <param name="depth">Elevation in effective pixels; larger values give softer, wider shadows.</param>
    public static void Apply(UIElement element, UIElement receiver, float depth)
    {
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(receiver);
        element.Shadow = shadow;
        element.Translation = new Vector3(0, 0, depth);
    }
}

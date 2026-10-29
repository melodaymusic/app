using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Helpers;

public static class Elevation
{
    public static void Apply(UIElement element, UIElement receiver, float depth)
    {
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(receiver);
        element.Shadow = shadow;
        element.Translation = new Vector3(0, 0, depth);
    }
}

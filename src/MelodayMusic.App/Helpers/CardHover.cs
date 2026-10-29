using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Helpers;

public static class CardHover
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(Motion.Standard);
    private const float PlateStartScale = 0.92f;
    private const float PlayRise = 8;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> CardRadius = [];

    public static void Attach(FrameworkElement plate, UIElement? play, double cardRadius = 8)
    {
        CardRadius.AddOrUpdate(plate, Services.UiScale.Size(cardRadius));
        plate.IsHitTestVisible = false;
        plate.Opacity = 0;
        plate.Scale = new Vector3(PlateStartScale, PlateStartScale, 1);
        ApplyMotion(plate, play);
        plate.SizeChanged += (_, e) => plate.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
        plate.Loaded += (_, _) => FitToHost(plate);

        if (play is null) return;
        play.Opacity = 0;
        play.Translation = new Vector3(0, PlayRise, 0);
    }

    public static void AttachPlay(UIElement play)
    {
        play.Opacity = 0;
        play.Translation = new Vector3(0, PlayRise, 0);
        if (!Motion.IsEnabled) return;
        play.OpacityTransition = Motion.Scalar(Duration.TotalMilliseconds);
        play.TranslationTransition = Motion.Vector(Duration.TotalMilliseconds);
    }

    public static void Set(FrameworkElement plate, UIElement? play, bool hovered)
    {
        if ((plate.OpacityTransition is not null) != Motion.IsEnabled) ApplyMotion(plate, play);
        if (hovered) FitToHost(plate);
        plate.Opacity = hovered ? 1 : 0;
        plate.Scale = hovered ? Vector3.One : new Vector3(PlateStartScale, PlateStartScale, 1);
        if (play is null) return;
        play.Opacity = hovered ? 1 : 0;
        play.Translation = hovered ? Vector3.Zero : new Vector3(0, PlayRise, 0);
        play.IsHitTestVisible = hovered;
    }

    private static void ApplyMotion(FrameworkElement plate, UIElement? play)
    {
        var ms = Duration.TotalMilliseconds;
        plate.OpacityTransition = Motion.Scalar(ms);
        plate.ScaleTransition = Motion.Vector(ms);
        if (play is null) return;
        play.OpacityTransition = Motion.Scalar(ms);
        play.TranslationTransition = Motion.Vector(ms);
    }

    private static void FitToHost(FrameworkElement plate)
    {
        for (var parent = VisualTreeHelper.GetParent(plate); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            var inner = CardRadius.TryGetValue(plate, out var r) ? (double)r : Services.UiScale.ArtworkRadius;
            if (parent is Controls.CardSlot slot)
            {
                slot.Adopt(plate, new CornerRadius(inner + slot.Inset.Left));
                return;
            }
            if (parent is not (SelectorItem or Button)) continue;
            var p = ((Control)parent).Padding;
            var margin = new Thickness(-p.Left, -p.Top, -p.Right, -p.Bottom);
            if (plate.Margin != margin) plate.Margin = margin;
            var radius = new CornerRadius(inner + p.Left);
            ((Control)parent).CornerRadius = radius;
            if (parent is ListViewItem host) host.Resources["ListViewItemCornerRadius"] = radius;
            if (plate is Border border && border.CornerRadius != radius) border.CornerRadius = radius;
            return;
        }
    }
}

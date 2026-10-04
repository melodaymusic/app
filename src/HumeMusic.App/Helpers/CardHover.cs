using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Helpers;

/// <summary>
/// The one hover every card shares (tiles, colour boards, billboards), in the manner of Spotify: a
/// soft rounded plate grows out from the card's centre to fill its container, while the play button
/// fades in and rises a few pixels into place, all together. Nothing is enlarged.
/// </summary>
public static class CardHover
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(Motion.Standard);
    private const float PlateStartScale = 0.92f;
    private const float PlayRise = 8;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> CardRadius = [];

    /// <summary>
    /// Sets up a card's plate and play button in their resting state. <paramref name="cardRadius"/> is
    /// the radius of the card's own shape (artwork 8, boards 16): the plate around it is that plus the
    /// container's padding, so it is concentric with the card whatever container holds it.
    /// </summary>
    public static void Attach(FrameworkElement plate, UIElement? play, double cardRadius = 8)
    {
        CardRadius.AddOrUpdate(plate, cardRadius);
        plate.IsHitTestVisible = false;
        plate.Opacity = 0;
        plate.Scale = new Vector3(PlateStartScale, PlateStartScale, 1);
        ApplyMotion(plate, play);
        // The plate grows from its centre; the centre point is visual only, so this never re-lays out.
        plate.SizeChanged += (_, e) => plate.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
        plate.Loaded += (_, _) => FitToHost(plate);

        if (play is null) return;
        play.Opacity = 0;
        play.Translation = new Vector3(0, PlayRise, 0);
    }

    /// <summary>A play button made after its card (on first hover), put in its resting state.</summary>
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
        // Motion may have been turned on or off since the card was made.
        if ((plate.OpacityTransition is not null) != Motion.IsEnabled) ApplyMotion(plate, play);
        // Grid items can be loaded before they reach their container; fit the plate to it at the latest here.
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

    /// <summary>
    /// The plate reaches out over the padding of the card's container (list item or button), with the
    /// card's radius plus that padding, so it fills exactly the space the card sits in.
    /// </summary>
    private static void FitToHost(FrameworkElement plate)
    {
        for (var parent = VisualTreeHelper.GetParent(plate); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            var inner = CardRadius.TryGetValue(plate, out var r) ? (double)r : 8;
            // In a grid the slot holds the plate itself: a card stretched to fill a grid item clips
            // anything reaching outside it, so the plate can't reach out from inside the card there.
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
            if (plate is Border border && border.CornerRadius != radius) border.CornerRadius = radius;
            return;
        }
    }
}

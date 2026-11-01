using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace MelodayMusic.Helpers;

public static class Anim
{
    private static readonly List<WeakReference<UIElement>> Appearing = [];
    private static readonly List<WeakReference<UIElement>> Fading = [];
    private static bool _watching;
    private static bool _enabled;

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not UIElement element || e.NewValue is not true) return;
            Track(Appearing, element);
            if (element is FrameworkElement framework)
                framework.Loaded += (_, _) => ApplyAppear(element);
            ApplyAppear(element);
        }));

    public static bool GetAppear(DependencyObject o) => (bool)o.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject o, bool value) => o.SetValue(AppearProperty, value);

    public static void SuppressExit(UIElement page) => Visit(page, element =>
        ElementCompositionPreview.SetImplicitHideAnimation(element, null));

    public static void RestoreAppear(UIElement page) => Visit(page, element =>
    {
        if (GetAppear(element)) ApplyAppear(element);
    });

    private static void Visit(DependencyObject root, Action<UIElement> apply)
    {
        if (root is UIElement element) apply(element);
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), apply);
    }

    private static void ApplyAppear(UIElement element)
    {
        if (!Motion.IsEnabled)
        {
            ElementCompositionPreview.SetImplicitShowAnimation(element, null);
            ElementCompositionPreview.SetImplicitHideAnimation(element, null);
            return;
        }
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));

        var fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.Target = "Opacity";
        fadeIn.InsertKeyFrame(0, 0);
        fadeIn.InsertKeyFrame(1, 1, ease);
        fadeIn.Duration = TimeSpan.FromMilliseconds(Motion.Standard);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.Target = "Translation";
        rise.InsertKeyFrame(0, new Vector3(0, 6, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = TimeSpan.FromMilliseconds(Motion.Standard);

        var show = compositor.CreateAnimationGroup();
        show.Add(fadeIn);
        show.Add(rise);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        ElementCompositionPreview.SetImplicitShowAnimation(element, show);

        var fadeOut = compositor.CreateScalarKeyFrameAnimation();
        fadeOut.Target = "Opacity";
        fadeOut.InsertKeyFrame(1, 0);
        fadeOut.Duration = TimeSpan.FromMilliseconds(Motion.Quick);
        ElementCompositionPreview.SetImplicitHideAnimation(element, fadeOut);
    }

    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade", typeof(bool), typeof(Anim), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not UIElement element || e.NewValue is not true) return;
            Track(Fading, element);
            ApplyFade(element);
        }));

    public static bool GetFade(DependencyObject o) => (bool)o.GetValue(FadeProperty);
    public static void SetFade(DependencyObject o, bool value) => o.SetValue(FadeProperty, value);

    private static void ApplyFade(UIElement element)
    {
        element.OpacityTransition = Motion.Scalar(Motion.Quick);
        switch (element)
        {
            case Border border: border.BackgroundTransition = Motion.Brush(Motion.Quick); break;
            case Panel panel: panel.BackgroundTransition = Motion.Brush(Motion.Quick); break;
            case ContentPresenter presenter: presenter.BackgroundTransition = Motion.Brush(Motion.Quick); break;
        }
    }

    private static void Track(List<WeakReference<UIElement>> list, UIElement element)
    {
        if (list.Count > 256 && list.Count % 256 == 0) list.RemoveAll(w => !w.TryGetTarget(out _));
        list.Add(new WeakReference<UIElement>(element));
        if (_watching) return;
        _watching = true;
        _enabled = Motion.IsEnabled;
        Motion.Changed += (_, _) =>
        {
            if (Motion.IsEnabled == _enabled) return;
            _enabled = Motion.IsEnabled;
            Refresh(Appearing, ApplyAppear);
            Refresh(Fading, ApplyFade);
        };
    }

    private static void Refresh(List<WeakReference<UIElement>> list, Action<UIElement> apply)
    {
        list.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in list)
            if (weak.TryGetTarget(out var element)) apply(element);
    }
}

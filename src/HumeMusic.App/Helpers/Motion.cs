using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace HumeMusic.Helpers;

/// <summary>
/// Whether the app moves: the listener's Motion setting (Settings) and Windows' own animation
/// effects together. With either off, moving artwork shows its still, the intro is skipped, and
/// transitions, fades and pulses become instant changes.
/// </summary>
public static class Motion
{
    private static readonly UISettings System = new();
    private static DispatcherQueue? _dispatcher;
    private static bool _setting = true;

    public static bool IsEnabled => _setting && System.AnimationsEnabled;

    // ------------------------------------------------------------------ the motion scale
    // Every transition in the app uses one of these durations (see docs/design-system.md). Small things
    // move quickly; larger surfaces take a little longer so the eye can follow them.

    /// <summary>Hover and press feedback, glyph swaps: felt rather than watched.</summary>
    public const double Quick = 120;
    /// <summary>Controls and content appearing, disappearing or changing state.</summary>
    public const double Standard = 200;
    /// <summary>Larger surfaces: panels, page content, headers condensing, sections expanding.</summary>
    public const double Emphasized = 320;
    /// <summary>Ambient colour behind the page crossfading between artworks.</summary>
    public const double Ambient = 700;
    /// <summary>A change the listener made and should watch happen, such as two rows becoming one.</summary>
    public const double Deliberate = 640;

    /// <summary>The window is the one in use (not in the background or minimised).</summary>
    public static bool IsWindowActive { get; private set; } = true;

    /// <summary>
    /// Whether ongoing motion (videos, drifting artwork) should run now: motion is on and the window is
    /// in use. Nothing keeps the compositor drawing frames for a window nobody is looking at.
    /// </summary>
    public static bool IsActive => IsEnabled && IsWindowActive;

    /// <summary>Called by the window as it is activated or deactivated.</summary>
    public static void SetWindowActive(bool active)
    {
        if (IsWindowActive == active) return;
        IsWindowActive = active;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Raised on the UI thread when motion turns on or off.</summary>
    public static event EventHandler? Changed;

    /// <summary>Called once at startup, on the UI thread, with the saved setting.</summary>
    public static void Initialize(bool setting)
    {
        _setting = setting;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        System.AnimationsEnabledChanged += (_, _) => _dispatcher?.TryEnqueue(() => Changed?.Invoke(null, EventArgs.Empty));
    }

    public static void Set(bool on)
    {
        if (_setting == on) return;
        _setting = on;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>A duration, or none with motion off.</summary>
    public static TimeSpan Duration(double milliseconds) => IsEnabled ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;

    /// <summary>An implicit transition, or none with motion off.</summary>
    public static ScalarTransition? Scalar(double milliseconds) =>
        IsEnabled ? new ScalarTransition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    public static Vector3Transition? Vector(double milliseconds) =>
        IsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    public static BrushTransition? Brush(double milliseconds) =>
        IsEnabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    /// <summary>Gives an element its opacity (and optionally translation) transitions, or takes them away.</summary>
    public static void Transitions(UIElement element, double milliseconds, bool translation = false)
    {
        element.OpacityTransition = Scalar(milliseconds);
        if (translation) element.TranslationTransition = Vector(milliseconds);
    }
}

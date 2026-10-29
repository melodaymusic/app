using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace MelodayMusic.Helpers;

public static class Motion
{
    private static readonly UISettings System = new();
    private static DispatcherQueue? _dispatcher;
    private static bool _setting = true;

    public static bool IsEnabled => _setting && System.AnimationsEnabled;

    public const double Quick = 120;
    public const double Standard = 200;
    public const double Emphasized = 320;
    public const double Ambient = 700;
    public const double Deliberate = 640;

    public static bool IsWindowActive { get; private set; } = true;

    public static bool IsActive => IsEnabled && IsWindowActive;

    public static void SetWindowActive(bool active)
    {
        if (IsWindowActive == active) return;
        IsWindowActive = active;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static event EventHandler? Changed;

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

    public static TimeSpan Duration(double milliseconds) => IsEnabled ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;

    public static ScalarTransition? Scalar(double milliseconds) =>
        IsEnabled ? new ScalarTransition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    public static Vector3Transition? Vector(double milliseconds) =>
        IsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    public static BrushTransition? Brush(double milliseconds) =>
        IsEnabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(milliseconds) } : null;

    public static void Transitions(UIElement element, double milliseconds, bool translation = false)
    {
        element.OpacityTransition = Scalar(milliseconds);
        if (translation) element.TranslationTransition = Vector(milliseconds);
    }
}

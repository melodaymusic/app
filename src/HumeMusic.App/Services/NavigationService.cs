using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Services;

/// <summary>Page navigation inside the main content frame.</summary>
public sealed class NavigationService
{
    private readonly ILogger<NavigationService> _log;
    private readonly NotificationService _notifications;
    private Frame? _frame;
    private object? _parameter;
    private (Type Page, object? Parameter, List<PageStackEntry> Back)? _pending;

    public event EventHandler<NavigationEventArgs>? Navigated;

    public NavigationService(ILogger<NavigationService> log, NotificationService notifications)
    {
        _log = log;
        _notifications = notifications;
    }

    /// <summary>
    /// A page that fails to build (a bad template, a control that throws while loading) is logged in
    /// full and reported, and the listener stays where they were instead of the app failing.
    /// </summary>
    private bool TryNavigate(Type page, object? parameter, NavigationTransitionInfo transition)
    {
        try
        {
            var clock = PerfMonitor.IsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
            var opened = _frame!.Navigate(page, parameter, transition);
            if (clock is not null) PerfMonitor.Opened(page.Name, clock.ElapsedMilliseconds, _frame.Content);
            return opened;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not open {Page}: {Detail}", page.Name, ex.InnerException?.ToString() ?? ex.Message);
            _notifications.ShowError("That page couldn't be opened", "The problem has been logged.", autoHide: true);
            return false;
        }
    }

    public void Attach(Frame frame)
    {
        if (_frame is not null) _frame.Navigated -= OnNavigated;
        _frame = frame;
        _frame.Navigated += OnNavigated;
        PerfMonitor.Start(frame.DispatcherQueue, _log);
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;
    public Type? CurrentPage => _frame?.CurrentSourcePageType;

    public void Navigate(Type page, object? parameter = null, bool clearHistory = false)
    {
        if (_frame is null) return;
        if (_frame.CurrentSourcePageType == page && parameter is null && !clearHistory) return;
        Transition(() =>
        {
            if (TryNavigate(page, parameter, Instant) && clearHistory) _frame.BackStack.Clear();
        });
    }

    public void NavigateRoot(Type page)
    {
        if (_frame is null) return;
        void Go()
        {
            if (_frame.CurrentSourcePageType != page) TryNavigate(page, null, Instant);
            _frame.BackStack.Clear();
            Navigated?.Invoke(this, null!);
        }
        if (_frame.CurrentSourcePageType == page) Go();
        else Transition(Go);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true) Transition(() => _frame.GoBack(Instant));
    }

    // ------------------------------------------------------------------ changing page

    private int _transition;

    /// <summary>
    /// A page change that never shows two pages at once: the page on show fades away, quickly, and only then is the next put in
    /// its place (it fades in, see <see cref="FadeIn"/>). The fade runs on the compositor, so it is smooth even when building
    /// the next page keeps the interface busy. Choosing another page meanwhile just changes where it ends up; a page that
    /// won't be left (it asked to stay) is shown again.
    /// </summary>
    private void Transition(Action navigate)
    {
        if (_frame?.Content is not Microsoft.UI.Xaml.UIElement old || !Helpers.Motion.IsEnabled)
        {
            navigate();
            return;
        }
        var version = ++_transition;
        var frame = _frame;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(old);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(1f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Quick);
        frame.IsHitTestVisible = false;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => frame.DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _transition) return;
            try
            {
                navigate();
            }
            finally
            {
                frame.IsHitTestVisible = true;
                // The page stayed (it asked not to be left, or the next couldn't be built): it is shown again.
                if (ReferenceEquals(frame.Content, old))
                {
                    visual.StopAnimation("Opacity");
                    visual.Opacity = 1;
                }
            }
        });
    }

    /// <summary>Pages simply appear, as on the Mac: no slide or entrance animation, whatever the Motion setting.</summary>
    private static NavigationTransitionInfo Instant => new SuppressNavigationTransitionInfo();

    /// <summary>Leaves an item's page if it is showing (the item was deleted): back, or to the list it came from.</summary>
    public void LeaveIfShowing(Type page, string ratingKey)
    {
        if (_frame?.CurrentSourcePageType != page || _parameter is not HumeMusic.Core.Models.MediaItem item || item.RatingKey != ratingKey) return;
        if (_frame.CanGoBack) GoBack();
        else NavigateRoot(typeof(Views.PlaylistsPage));
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        _parameter = e.Parameter;
        FadeIn(e.Content as Microsoft.UI.Xaml.UIElement);
        Navigated?.Invoke(this, e);
    }

    /// <summary>
    /// The new page fades in over the moment it takes to lay out, so moving between pages is a soft
    /// change rather than a cut. Nothing slides: the page stays where it is and only its opacity eases.
    /// </summary>
    private static void FadeIn(Microsoft.UI.Xaml.UIElement? page)
    {
        if (page is null) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        // A page kept from before (the cached ones) was left faded away: without motion it is simply shown.
        if (!Helpers.Motion.IsEnabled)
        {
            visual.Opacity = 1;
            return;
        }
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Standard);
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>Remembers the current page and history so a rebuilt shell can return to it.</summary>
    public void SaveForRestore()
    {
        _pending = _frame?.CurrentSourcePageType is { } page
            ? (page, _parameter, _frame.BackStack.Select(b => new PageStackEntry(b.SourcePageType, b.Parameter, b.NavigationTransitionInfo)).ToList())
            : null;
    }

    /// <summary>Returns to the page saved by <see cref="SaveForRestore"/>, if any.</summary>
    public bool RestorePending()
    {
        if (_frame is null || _pending is not { } p) return false;
        _pending = null;
        TryNavigate(p.Page, p.Parameter, new SuppressNavigationTransitionInfo());
        _frame.BackStack.Clear();
        foreach (var entry in p.Back) _frame.BackStack.Add(entry);
        Navigated?.Invoke(this, null!);
        return true;
    }
}

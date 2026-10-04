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
        if (TryNavigate(page, parameter, Instant) && clearHistory)
            _frame.BackStack.Clear();
    }

    public void NavigateRoot(Type page)
    {
        if (_frame is null) return;
        if (_frame.CurrentSourcePageType != page)
            TryNavigate(page, null, Instant);
        _frame.BackStack.Clear();
        Navigated?.Invoke(this, null!);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true) _frame.GoBack(Instant);
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
        if (page is null || !Helpers.Motion.IsEnabled) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized);
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

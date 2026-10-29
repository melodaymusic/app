using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Services;

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
        CancelTransition();
        if (_frame is not null) _frame.Navigated -= OnNavigated;
        _frame = frame;
        _frame.Navigated += OnNavigated;
        PerfMonitor.Start(frame.DispatcherQueue, _log);
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;
    public Type? CurrentPage => _frame?.CurrentSourcePageType;

    /// <summary>The item the current page shows (an album, playlist, mix…), if it shows one.</summary>
    public MelodayMusic.Core.Models.MediaItem? CurrentItem => _parameter as MelodayMusic.Core.Models.MediaItem;

    public void Navigate(Type page, object? parameter = null, bool clearHistory = false)
    {
        if (_frame is null) return;
        if (_frame.CurrentSourcePageType == page && parameter is null && !clearHistory)
        {
            CancelTransition();
            return;
        }
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
        if (_frame.CurrentSourcePageType == page) { CancelTransition(); Go(); }
        else Transition(Go);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true) Transition(() => _frame.GoBack(Instant));
    }

    private int _transition;

    private void CancelTransition()
    {
        ++_transition;
        if (_frame is null) return;
        _frame.IsHitTestVisible = true;
        if (_frame.Content is Microsoft.UI.Xaml.UIElement current)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(current);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
        }
    }

    private void Transition(Action navigate)
    {
        var version = ++_transition;
        if (_frame?.Content is not Microsoft.UI.Xaml.UIElement old || !Helpers.Motion.IsEnabled)
        {
            if (_frame is not null) _frame.IsHitTestVisible = true;
            navigate();
            return;
        }
        var frame = _frame;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(old);
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(1f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Quick);
        frame.IsHitTestVisible = false;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) => frame.DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _transition) return;
            try
            {
                Helpers.Anim.SuppressExit(old);
                navigate();
            }
            finally
            {
                frame.IsHitTestVisible = true;
                if (ReferenceEquals(frame.Content, old))
                {
                    Helpers.Anim.RestoreAppear(old);
                    visual.StopAnimation("Opacity");
                    visual.Opacity = 1;
                }
            }
        });
        batch.End();
    }

    private static NavigationTransitionInfo Instant => new SuppressNavigationTransitionInfo();

    public void LeaveIfShowing(Type page, string ratingKey)
    {
        if (_frame?.CurrentSourcePageType != page || _parameter is not MelodayMusic.Core.Models.MediaItem item || item.RatingKey != ratingKey) return;
        if (_frame.CanGoBack) GoBack();
        else NavigateRoot(typeof(Views.PlaylistsPage));
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        _parameter = e.Parameter;
        FadeIn(e.Content as Microsoft.UI.Xaml.UIElement);
        Navigated?.Invoke(this, e);
    }

    private void FadeIn(Microsoft.UI.Xaml.UIElement? page)
    {
        if (page is null) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(page);
        visual.StopAnimation("Opacity");
        if (!Helpers.Motion.IsEnabled)
        {
            visual.Opacity = 1;
            return;
        }
        visual.Opacity = 0;
        var frame = _frame;
        var version = _transition;
        page.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
        if (version != _transition || !ReferenceEquals(frame?.Content, page)) return;
        if (!Helpers.Motion.IsEnabled) { visual.Opacity = 1; return; }
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Standard);
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", fade);
        batch.Completed += (_, _) => page.DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _transition || !ReferenceEquals(frame?.Content, page)) return;
            visual.Opacity = 1;
            visual.StopAnimation("Opacity");
        });
        batch.End();
        });
    }

    public void SaveForRestore()
    {
        _pending = _frame?.CurrentSourcePageType is { } page
            ? (page, _parameter, _frame.BackStack.Select(b => new PageStackEntry(b.SourcePageType, b.Parameter, b.NavigationTransitionInfo)).ToList())
            : null;
    }

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

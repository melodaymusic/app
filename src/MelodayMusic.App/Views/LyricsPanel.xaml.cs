using System.ComponentModel;
using System.Numerics;
using MelodayMusic.Core.Models;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace MelodayMusic.Views;

public sealed partial class LyricsPanel : UserControl
{
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan HandScrollHold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan IntroDots = TimeSpan.FromSeconds(4);
    private const double ReadingPoint = 0.28;
    private const double StaggerMs = 38;

    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LyricsService _service = App.Services.GetRequiredService<LyricsService>();
    private readonly List<FrameworkElement> _rows = [];
    private TrackLyrics? _lyrics;
    private bool _intro;
    private string? _shownKey;
    private int _active = -2;
    private int? _backwardSeen;
    private bool _jumped;
    private double _offset;
    private double _handOffset;
    private DateTime _handScrolledAt = DateTime.MinValue;
    private int _version;

    private Compositor? _compositor;

    public LyricsPanel()
    {
        InitializeComponent();
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheel), true);
        Scroller.DirectManipulationStarted += (_, _) => _handScrolledAt = DateTime.UtcNow;
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            _service.Changed += OnLyricsChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _service.Changed -= OnLyricsChanged;
        };
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Refresh());
    }

    private void OnLyricsChanged(object? sender, string ratingKey) => DispatcherQueue.TryEnqueue(() =>
    {
        if (ratingKey != _shownKey) return;
        _shownKey = null;
        Refresh();
    });

    private void OnTimingClick(object sender, RoutedEventArgs e)
    {
        if (_player.CurrentTrack is not { } track) return;
        var edit = _service.EditOf(track);
        var offset = edit?.OffsetMs ?? 0;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            IsEnabled = false,
            Text = offset == 0 ? "Timing as the source has it" : $"Shifted {(offset > 0 ? "later" : "earlier")} by {Math.Abs(offset) / 1000.0:0.0} s",
        });
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Lyrics earlier (0.1 s)", () => _service.Nudge(track, -100));
        Add("Lyrics later (0.1 s)", () => _service.Nudge(track, 100));
        if (offset != 0) Add("Undo the shift", () => _service.Nudge(track, -offset));
        if (edit?.Lines is { Count: > 0 }) Add("Use the original lyrics", () => _service.ClearEdits(track));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Time the lyrics yourself…", () => App.Services.GetRequiredService<LibraryActions>().OpenLyricStudio(track));
        menu.ShowAt(TimingButton);
    }

    private bool IsShown => Visibility == Visibility.Visible;
    private bool Floating => _lyrics is { IsSynced: true };

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsShown) return;
        if (e.PropertyName == nameof(PlayerService.CurrentTrack)) Refresh();
        else if (e.PropertyName == nameof(PlayerService.Position)) Follow(animate: true);
    }

    private void Refresh()
    {
        if (!IsShown) return;
        var track = _player.CurrentTrack;
        if (track?.RatingKey == _shownKey && _shownKey is not null) return;
        _ = LoadAsync(track);
    }

    private async Task LoadAsync(Track? track)
    {
        var version = ++_version;
        _shownKey = track?.RatingKey;
        _lyrics = null;
        _active = -2;
        _rows.Clear();
        Lines.Children.Clear();
        Credit.Visibility = Visibility.Collapsed;
        if (track is null)
        {
            ShowEmpty("Nothing Playing", "Lyrics appear here while a song plays.");
            return;
        }
        State.IsEmpty = false;
        State.IsLoading = true;
        var lyrics = await _service.GetAsync(track);
        if (version != _version) return;
        State.IsLoading = false;
        if (lyrics is null)
        {
            _shownKey = null;
            ShowEmpty("No Lyrics", "Neither Plex nor LRCLIB has lyrics for this song.");
            return;
        }
        if (lyrics.IsInstrumental)
        {
            ShowEmpty("Instrumental", "This song has no singing.");
            return;
        }
        _lyrics = lyrics;
        Credit.Text = lyrics.Source switch
        {
            LyricsSource.LrcLib => "Lyrics from LRCLIB",
            LyricsSource.Custom => "Lyrics timed by you",
            _ => "Lyrics from LyricFind, through Plex",
        };
        Credit.Visibility = Visibility.Visible;
        State.IsEmpty = false;
        Build(lyrics);
        Scroller.ChangeView(null, 0, null, true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Follow(animate: false, force: true));
    }

    private void ShowEmpty(string title, string text)
    {
        State.IsLoading = false;
        State.EmptyTitle = title;
        State.EmptyText = text;
        State.IsEmpty = true;
    }

    private void Build(TrackLyrics lyrics)
    {
        var synced = lyrics.IsSynced;
        Scroller.VerticalScrollMode = synced ? ScrollMode.Disabled : ScrollMode.Auto;
        Scroller.VerticalScrollBarVisibility = synced ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        _handOffset = 0;
        _offset = 0;
        _intro = synced && lyrics.Lines[0].Start > IntroDots;
        if (_intro) Add(Dots(), null);
        foreach (var line in lyrics.Lines)
        {
            FrameworkElement row = synced && line.Text.Length == 0 ? Dots() : new TextBlock
            {
                Text = line.Text.Length == 0 ? "♪" : line.Text,
                Style = (Style)Application.Current.Resources["MelodayLyricText"],
            };
            Add(row, synced ? line.Start : null);
        }
        Lines.Padding = new Thickness(12, 8, 12, synced ? 400 : 24);

        void Add(FrameworkElement row, TimeSpan? start)
        {
            if (synced)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(row, true);
                ElementCompositionPreview.GetElementVisual(row).Opacity = 0.3f;
            }
            if (start is { } at)
            {
                row.Tapped += (_, _) =>
                {
                    _handScrolledAt = DateTime.MinValue;
                    _handOffset = 0;
                    _jumped = true;
                    _player.Seek(at);
                };
            }
            _rows.Add(row);
            Lines.Children.Add(row);
        }
    }

    private static FrameworkElement Dots()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 3; i++)
            dots.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)Application.Current.Resources["MelodayTextPrimaryBrush"],
            });
        return dots;
    }

    private void Follow(bool animate, bool force = false)
    {
        if (_lyrics is not { IsSynced: true } lyrics || _rows.Count == 0) return;
        var index = lyrics.IndexAt(_player.Position + Lead);
        var active = index < 0 ? (_intro ? 0 : -1) : index + (_intro ? 1 : 0);
        var handHeld = DateTime.UtcNow - _handScrolledAt < HandScrollHold;
        if (active < _active && !force && !_jumped)
        {
            if (_backwardSeen != active)
            {
                _backwardSeen = active;
                return;
            }
        }
        _backwardSeen = null;
        _jumped = false;
        var returning = !handHeld && _handOffset != 0;
        if (active == _active && !force && !returning) return;
        var moved = active != _active;
        _active = active;

        for (var i = 0; i < _rows.Count; i++)
        {
            var distance = active < 0 ? i + 1 : Math.Abs(i - active);
            Fade(_rows[i], distance == 0 ? 1f : (float)Math.Max(0.2, 0.46 - 0.06 * (distance - 1)), animate);
            Breathe(_rows[i], distance == 0 && _rows[i] is StackPanel);
            ScaleRow(_rows[i], distance == 0 ? 1f : 0.965f, animate);
        }

        if (handHeld) return;
        _handOffset = 0;
        var anchor = active < 0 ? 0 : active;
        var top = RowTop(anchor);
        _offset = Math.Max(0, top - Viewport.ActualHeight * ReadingPoint);
        Place(anchor, animate && (moved || force || returning));
    }

    private double RowTop(int row) => _rows[row].ActualOffset.Y;

    private void Place(int anchor, bool animate, bool stagger = true)
    {
        _compositor ??= ElementCompositionPreview.GetElementVisual(this).Compositor;
        var target = new Vector3(0, (float)-(_offset + _handOffset), 0);
        for (var i = 0; i < _rows.Count; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_rows[i]);
            if (!animate || !Motion.IsEnabled)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", target);
                continue;
            }
            var spring = _compositor.CreateSpringVector3Animation();
            spring.Target = "Translation";
            spring.FinalValue = target;
            spring.DampingRatio = 0.82f;
            spring.Period = TimeSpan.FromMilliseconds(68);
            var behind = stagger ? Math.Max(0, i - anchor + 1) : 0;
            spring.DelayTime = TimeSpan.FromMilliseconds(Math.Min(behind, 10) * StaggerMs);
            spring.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", spring);
        }
    }

    private void Fade(FrameworkElement row, float opacity, bool animate)
    {
        var visual = ElementCompositionPreview.GetElementVisual(row);
        _compositor ??= visual.Compositor;
        if (!animate || !Motion.IsEnabled)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = opacity;
            return;
        }
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, opacity, _compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1)));
        fade.Duration = TimeSpan.FromMilliseconds(Motion.Emphasized);
        visual.StartAnimation("Opacity", fade);
    }

    private void ScaleRow(FrameworkElement row, float scale, bool animate)
    {
        var visual = ElementCompositionPreview.GetElementVisual(row);
        visual.CenterPoint = new Vector3(0, (float)row.ActualHeight / 2, 0);
        var target = new Vector3(scale, scale, 1);
        if (!animate || !Motion.IsEnabled || _compositor is null)
        {
            visual.StopAnimation("Scale");
            visual.Scale = target;
            return;
        }
        var spring = _compositor.CreateSpringVector3Animation();
        spring.Target = "Scale";
        spring.FinalValue = target;
        spring.DampingRatio = 0.9f;
        spring.Period = TimeSpan.FromMilliseconds(60);
        visual.StartAnimation("Scale", spring);
    }

    private void Breathe(FrameworkElement row, bool on)
    {
        if (row is not StackPanel dots) return;
        foreach (var dot in dots.Children.OfType<Ellipse>())
        {
            var visual = ElementCompositionPreview.GetElementVisual(dot);
            visual.CenterPoint = new Vector3(4.5f, 4.5f, 0);
            if (!on || !Motion.IsActive || _compositor is null)
            {
                visual.StopAnimation("Scale");
                visual.Scale = Vector3.One;
                continue;
            }
            var breath = _compositor.CreateVector3KeyFrameAnimation();
            breath.InsertKeyFrame(0, Vector3.One);
            breath.InsertKeyFrame(0.5f, new Vector3(1.35f, 1.35f, 1));
            breath.InsertKeyFrame(1, Vector3.One);
            breath.Duration = TimeSpan.FromMilliseconds(1800);
            breath.IterationBehavior = AnimationIterationBehavior.Forever;
            breath.DelayTime = TimeSpan.FromMilliseconds(dots.Children.IndexOf(dot) * 180);
            visual.StartAnimation("Scale", breath);
        }
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        _handScrolledAt = DateTime.UtcNow;
        if (!Floating || _rows.Count == 0) return;
        var delta = e.GetCurrentPoint(Scroller).Properties.MouseWheelDelta;
        var content = Lines.ActualHeight - 400;
        _handOffset = Math.Clamp(_handOffset - delta * 0.6, -_offset, Math.Max(0, content - _offset));
        Place(_active < 0 ? 0 : _active, animate: true, stagger: false);
        e.Handled = true;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Floating) Follow(animate: false, force: true);
        });
    }
}

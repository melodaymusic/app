using System.ComponentModel;
using System.Numerics;
using HumeMusic.Core.Models;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HumeMusic.Views;

/// <summary>
/// The playing song's lyrics, as Apple Music shows them. Synced lyrics float: the line being sung sits
/// at the reading point, bright and full size; each line glides to its place on its own spring, the
/// ones nearest the singing first and the rest a beat behind, so the lyrics drift up like a list
/// being carried rather than scrolled. Lines away from the singing recede, dimmer and a little smaller
/// the further they are. Breaks in the singing, and a long
/// intro, show three breathing dots. Scrolling by hand lets the lyrics be read freely for a few
/// seconds; pressing a line plays from it. Plain lyrics scroll as ordinary text.
/// </summary>
public sealed partial class LyricsPanel : UserControl
{
    /// <summary>Lines light a moment early, so the eye arrives with the voice.</summary>
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(0.25);
    /// <summary>After scrolling by hand, the lyrics stay where they were put this long.</summary>
    private static readonly TimeSpan HandScrollHold = TimeSpan.FromSeconds(4);
    /// <summary>A song this long before its first line opens with the dots.</summary>
    private static readonly TimeSpan IntroDots = TimeSpan.FromSeconds(4);
    /// <summary>Where the line being sung sits, as a share of the panel's height.</summary>
    private const double ReadingPoint = 0.28;
    /// <summary>Each line further from the singing starts moving this much later.</summary>
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
            Refresh();
        };
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
        // Lyrics are only fetched while the panel is shown.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Refresh());
    }

    private bool IsShown => Visibility == Visibility.Visible;
    private bool Floating => _lyrics is { IsSynced: true };

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsShown) return;
        if (e.PropertyName == nameof(PlayerService.CurrentTrack)) Refresh();
        else if (e.PropertyName == nameof(PlayerService.Position)) Follow(animate: true);
    }

    /// <summary>Shows the playing song's lyrics (fetching them if need be), unless they already are.</summary>
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
            // Not remembered as missing for long: the panel asks again when this song comes back.
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
        Credit.Text = lyrics.Source == LyricsSource.LrcLib ? "Lyrics from LRCLIB" : "Lyrics from LyricFind, through Plex";
        Credit.Visibility = Visibility.Visible;
        State.IsEmpty = false;
        Build(lyrics);
        Scroller.ChangeView(null, 0, null, true);
        // After the lines have been measured.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Follow(animate: false, force: true));
    }

    private void ShowEmpty(string title, string text)
    {
        State.IsLoading = false;
        State.EmptyTitle = title;
        State.EmptyText = text;
        State.IsEmpty = true;
    }

    // ------------------------------------------------------------------ building the lines

    private void Build(TrackLyrics lyrics)
    {
        var synced = lyrics.IsSynced;
        // Floating lyrics are moved by the panel, not scrolled.
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
                Style = (Style)Application.Current.Resources["HumeLyricText"],
            };
            Add(row, synced ? line.Start : null);
        }
        // Room below the last line so it, too, can rise to the reading point.
        Lines.Padding = new Thickness(12, 8, 12, synced ? 400 : 24);

        void Add(FrameworkElement row, TimeSpan? start)
        {
            if (synced)
            {
                // Moved, faded and sized by the compositor alone (XAML transitions on the same element
                // would refuse composition animations).
                ElementCompositionPreview.SetIsTranslationEnabled(row, true);
                ElementCompositionPreview.GetElementVisual(row).Opacity = 0.3f;
            }
            if (start is { } at)
            {
                // A press on a line plays the song from it.
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

    /// <summary>The three dots of a break in the singing.</summary>
    private static FrameworkElement Dots()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 3; i++)
            dots.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)Application.Current.Resources["HumeTextPrimaryBrush"],
            });
        return dots;
    }

    // ------------------------------------------------------------------ following the song

    /// <summary>
    /// Lights the line being sung and carries the lyrics so it sits at the reading point: every row
    /// glides on its own spring, those further from the singing starting later.
    /// </summary>
    private void Follow(bool animate, bool force = false)
    {
        if (_lyrics is not { IsSynced: true } lyrics || _rows.Count == 0) return;
        var index = lyrics.IndexAt(_player.Position + Lead);
        var active = index < 0 ? (_intro ? 0 : -1) : index + (_intro ? 1 : 0);
        var handHeld = DateTime.UtcNow - _handScrolledAt < HandScrollHold;
        // Going back is believed only when it's seen twice running (or the listener chose a line): a
        // single stray reading (the position briefly at 0 while the player re-buffers) mustn't send
        // the lyrics to the top and back.
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
        // Back from reading by hand: the lyrics glide home to the singing.
        var returning = !handHeld && _handOffset != 0;
        if (active == _active && !force && !returning) return;
        var moved = active != _active;
        _active = active;

        for (var i = 0; i < _rows.Count; i++)
        {
            var distance = active < 0 ? i + 1 : Math.Abs(i - active);
            Fade(_rows[i], distance == 0 ? 1f : (float)Math.Max(0.2, 0.46 - 0.06 * (distance - 1)), animate);
            Breathe(_rows[i], distance == 0 && _rows[i] is StackPanel);
            Scale(_rows[i], distance == 0 ? 1f : 0.965f, animate);
        }

        if (handHeld) return;
        _handOffset = 0;
        var anchor = active < 0 ? 0 : active;
        var top = RowTop(anchor);
        _offset = Math.Max(0, top - Viewport.ActualHeight * ReadingPoint);
        Place(anchor, animate && (moved || force || returning));
    }

    /// <summary>A row's place in the lines' own layout (unaffected by the floating, which is composition only).</summary>
    private double RowTop(int row) => _rows[row].ActualOffset.Y;

    /// <summary>Moves every row to the current offset, staggered outward from <paramref name="anchor"/>.</summary>
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
            // Lines above the singing go with it; those below follow, one after another.
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

    private void Scale(FrameworkElement row, float scale, bool animate)
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

    /// <summary>The dots of the break being heard swell and settle, like breathing.</summary>
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

    // ------------------------------------------------------------------ scrolling by hand

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        _handScrolledAt = DateTime.UtcNow;
        if (!Floating || _rows.Count == 0) return;
        var delta = e.GetCurrentPoint(Scroller).Properties.MouseWheelDelta;
        var content = Lines.ActualHeight - 400;
        _handOffset = Math.Clamp(_handOffset - delta * 0.6, -_offset, Math.Max(0, content - _offset));
        // Scrolled by hand: all the lines move together, gliding a little behind the wheel.
        Place(_active < 0 ? 0 : _active, animate: true, stagger: false);
        e.Handled = true;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // After layout settles (never change layout inside SizeChanged).
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Floating) Follow(animate: false, force: true);
        });
    }
}

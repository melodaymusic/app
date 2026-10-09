using HumeMusic.Core.Lyrics;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace HumeMusic.Views;

/// <summary>
/// Timing a song's lyrics by hand: the song plays from the start, the next word (or line) is shown, and the listener presses the
/// space bar as it begins, keeping it down through a word that is held. The presses become lyrics timed word by word (see
/// <see cref="StudioSession"/>), kept on this computer and used in place of the source's by the lyrics panel and the lyric pictures.
/// While timing, the song stays on itself: at its end playback stops (it neither starts over nor moves to the next song), and the
/// listener is asked whether to keep the timing; so they are if they move to another song or page.
/// </summary>
public sealed partial class LyricStudioPage : Page
{
    /// <summary>"Undo" takes the song back this far before the last tap, so the word can be tapped again.</summary>
    private static readonly TimeSpan UndoBack = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SeekBack = TimeSpan.FromSeconds(5);
    /// <summary>The hold bar fills over this long (a word held longer than this just shows full).</summary>
    private static readonly TimeSpan HoldBarFills = TimeSpan.FromSeconds(4);

    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LyricsService _lyrics = App.Services.GetRequiredService<LyricsService>();
    private Track? _track;
    private StudioSession? _session;
    // The word being held (the key or the button is down): it is the active word, and the next isn't asked for until it lets go.
    private bool _holding;
    private DateTime _heldSince;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _holdTimer;
    private bool _asking, _leaving;

    public LyricStudioPage()
    {
        InitializeComponent();
        // Tunnelling, so the space bar is a tap (on the key going down, when the finger meets it) wherever the focus is,
        // and a focused button doesn't also click when it comes up.
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        // The Tap button is a space bar for the mouse: held down, it holds the word.
        TapButton.AddHandler(PointerPressedEvent, new PointerEventHandler(OnTapPressed), true);
        TapButton.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnTapReleased), true);
        TapButton.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnTapReleased), true);
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            _player.StoppedOnSong += OnSongEnded;
        };
        Unloaded += (_, _) =>
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _player.StoppedOnSong -= OnSongEnded;
            _player.StayOnSong = false;
            StopHoldBar();
        };
    }

    private bool Timing => TimingPanel.Visibility == Visibility.Visible;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _leaving = false;
        if (e.Parameter is not Track track)
        {
            App.Services.GetRequiredService<NavigationService>().GoBack();
            return;
        }
        _track = track;
        Header.Title = track.Title;
        Header.Subtitle = track.DisplayArtist;
        await LoadWordsAsync();
    }

    /// <summary>Leaving with timing that hasn't been kept: stay, and ask first.</summary>
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (_leaving || !Timing || _session is not { Tapped: > 0 }) return;
        e.Cancel = true;
        var (mode, type, parameter, frame) = (e.NavigationMode, e.SourcePageType, e.Parameter, Frame);
        _ = AskThenAsync(canKeep: true, afterwards: () =>
        {
            _leaving = true;
            if (mode == NavigationMode.Back) frame?.GoBack();
            else frame?.Navigate(type, parameter);
        });
    }

    /// <summary>The words to time: the song's lyrics as they are now (the source's, or the ones timed here before).</summary>
    private async Task LoadWordsAsync()
    {
        if (_track is not { } track) return;
        var lyrics = await _lyrics.GetAsync(track);
        var lines = lyrics?.Lines.Select(l => l.Text.Trim()).Where(l => l.Length > 0).ToList() ?? [];
        LyricsBox.Text = string.Join("\n", lines);
        SourceNote.Text = lyrics is null || lines.Count == 0
            ? "No lyrics were found for this song."
            : lyrics.Source switch
            {
                LyricsSource.Custom => "These are the lyrics you timed.",
                LyricsSource.LrcLib => "From LRCLIB.",
                _ => "From LyricFind, through Plex.",
            };
        OriginalButton.Visibility = _lyrics.EditOf(track) is null ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = lines.Count > 0;
    }

    private void OnLyricsChanged(object sender, TextChangedEventArgs e) => StartButton.IsEnabled = LyricsBox.Text.Any(char.IsLetterOrDigit);

    // ------------------------------------------------------------------ setting up

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        _session = new StudioSession(LyricsBox.Text, ModeBox.SelectedIndex == 1 ? StudioMode.Lines : StudioMode.Words);
        if (_session.UnitCount == 0) return;
        SetupPanel.Visibility = Visibility.Collapsed;
        TimingPanel.Visibility = Visibility.Visible;
        // From now the song stays on itself: it won't start over or move on at its end.
        _player.StayOnSong = true;
        StatusText.Text = string.Empty;
        // The song starts from the beginning (it is played first if something else is on).
        if (_player.CurrentTrack?.RatingKey != track.RatingKey) await _player.PlayTracksAsync([track]);
        else
        {
            _player.Seek(TimeSpan.Zero);
            if (!_player.IsPlaying) _player.TogglePlayPause();
        }
        UpdateView();
        TapButton.Focus(FocusState.Programmatic);
    }

    /// <summary>Back to the words (nothing tapped is kept).</summary>
    private void ShowSetup()
    {
        _session = null;
        _player.StayOnSong = false;
        StopHolding();
        TimingPanel.Visibility = Visibility.Collapsed;
        SetupPanel.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = false;
    }

    private void OnEditWords(object sender, RoutedEventArgs e)
    {
        if (_session is { Tapped: > 0 }) _ = AskThenAsync(canKeep: true, afterwards: ShowSetup);
        else ShowSetup();
    }

    private void OnStartOver(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        StopHolding();
        _session.Reset();
        _player.Seek(TimeSpan.Zero);
        if (!_player.IsPlaying) _player.TogglePlayPause();
        UpdateView();
    }

    // ------------------------------------------------------------------ timing

    private bool SongIsOn => _track is { } track && _player.CurrentTrack?.RatingKey == track.RatingKey;

    /// <summary>Marks the next word as beginning now. Returns whether it counted. With <paramref name="hold"/> the word is being held until <see cref="Release"/>.</summary>
    private bool Tap(bool hold)
    {
        if (_session is null || !Timing) return false;
        if (!SongIsOn)
        {
            StatusText.Text = "This song isn't playing. Press Play, or Start over, to play it from the start.";
            return false;
        }
        if (!_session.Tap(_player.PrecisePosition)) return false;
        // The word just pressed is the one being timed until the key comes up.
        if (hold) StartHolding();
        UpdateView();
        return true;
    }

    /// <summary>The space bar (or the button) came up: the word it was down for is done, and a long one is kept as a held word.</summary>
    private void Release()
    {
        if (!_holding) return;
        var held = _session?.Release(_player.PrecisePosition) == true;
        StopHolding();
        StatusText.Text = held ? "Held word timed." : string.Empty;
        UpdateView();
    }

    private void StartHolding()
    {
        _holding = true;
        _heldSince = DateTime.UtcNow;
        HoldBar.Value = 0;
        HoldBar.Opacity = 1;
        _holdTimer ??= DispatcherQueue.CreateTimer();
        _holdTimer.Interval = TimeSpan.FromMilliseconds(33);
        _holdTimer.Tick -= OnHoldTick;
        _holdTimer.Tick += OnHoldTick;
        _holdTimer.Start();
    }

    private void StopHolding()
    {
        _holding = false;
        StopHoldBar();
    }

    private void StopHoldBar()
    {
        _holdTimer?.Stop();
        HoldBar.Opacity = 0;
        HoldBar.Value = 0;
    }

    /// <summary>The bar fills while the word is held; once it has been down long enough to count as held the hint says so.</summary>
    private void OnHoldTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_holding) return;
        var elapsed = DateTime.UtcNow - _heldSince;
        HoldBar.Value = Math.Min(1, elapsed.TotalSeconds / HoldBarFills.TotalSeconds);
        NowHint.Text = elapsed >= StudioSession.HoldThreshold
            ? "Holding. Let go when the word ends."
            : "Let go now for a short word, or keep holding for a long one.";
    }

    // The mouse's tap: pressing counts the word, and holding the button through it holds the word.
    private bool _pointerDown, _pointerTapped;

    private void OnTapPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Tap(hold: true)) _pointerDown = _pointerTapped = true;
    }

    private void OnTapReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerDown) return;
        _pointerDown = false;
        Release();
    }

    private void OnTap(object sender, RoutedEventArgs e)
    {
        // A click that came from a press has already tapped; one from the keyboard or assistive technology hasn't (and has no hold).
        if (_pointerTapped) _pointerTapped = false;
        else Tap(hold: false);
        TapButton.Focus(FocusState.Programmatic);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        PlayOrPause();
        TapButton.Focus(FocusState.Programmatic);
    }

    private void PlayOrPause()
    {
        if (!SongIsOn && _track is { } track) _ = _player.PlayTracksAsync([track]);
        else _player.TogglePlayPause();
    }

    /// <summary>Takes back the last tap, and the song a moment before it, so that word is tapped again as it is sung.</summary>
    private void Undo()
    {
        if (_session?.LastTap is not { } last) return;
        StopHolding();
        var target = last > UndoBack ? last - UndoBack : TimeSpan.Zero;
        _session.RewindTo(target);
        _player.Seek(target);
        UpdateView();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        Undo();
        TapButton.Focus(FocusState.Programmatic);
    }

    private void GoBackFive()
    {
        if (_session is null) return;
        StopHolding();
        var now = _player.PrecisePosition;
        var target = now > SeekBack ? now - SeekBack : TimeSpan.Zero;
        _session.RewindTo(target);
        _player.Seek(target);
        UpdateView();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        GoBackFive();
        TapButton.Focus(FocusState.Programmatic);
    }

    // The space bar is the only key: pressed as a word begins, and held through a word that is held.
    private bool _spaceDown;

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!Timing || e.Key != VirtualKey.Space) return;
        // Holding the key repeats it: only the press counts.
        if (!e.KeyStatus.WasKeyDown && Tap(hold: true)) _spaceDown = true;
        e.Handled = true;
    }

    // A focused button would click as the space bar comes up; it has already counted as a tap on the way down.
    private void OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!Timing || e.Key != VirtualKey.Space) return;
        if (_spaceDown)
        {
            _spaceDown = false;
            Release();
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ the song ending, or the listener leaving it

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.IsPlaying)) DispatcherQueue.TryEnqueue(UpdatePlayLabel);
        // Moved to another song (the player bar's Next or Previous, or another song chosen): stop, and ask about the timing.
        else if (e.PropertyName == nameof(PlayerService.CurrentTrack)) DispatcherQueue.TryEnqueue(OnTrackChanged);
    }

    private void OnTrackChanged()
    {
        UpdatePlayLabel();
        if (!Timing || SongIsOn || _session is null) return;
        _player.Pause();
        if (_session.Tapped > 0) _ = AskThenAsync(canKeep: false, afterwards: ShowSetup);
        else ShowSetup();
    }

    /// <summary>The song reached its end while being timed: it has stopped, and the timing is asked about.</summary>
    private void OnSongEnded(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!Timing || _session is null) return;
        StopHolding();
        _spaceDown = _pointerDown = false;
        UpdatePlayLabel();
        if (_session.Tapped > 0) _ = AskThenAsync(canKeep: true, afterwards: ShowSetup, songEnded: true);
        else StatusText.Text = "The song has ended.";
    });

    /// <summary>Asks whether to keep the timing so far. Saving keeps it and goes on to <paramref name="afterwards"/>; not saving drops it and goes on; keeping on timing stays.</summary>
    private async Task AskThenAsync(bool canKeep, Action afterwards, bool songEnded = false)
    {
        if (_asking || _session is not { } session || _track is not { } track) return;
        _asking = true;
        // The song stops while the timing is asked about.
        if (SongIsOn && _player.IsPlaying) _player.Pause();
        // The key may come up while the dialog has the focus: nothing is left held.
        StopHolding();
        _spaceDown = _pointerDown = false;
        try
        {
            var all = session.UnitCount;
            var done = session.Tapped;
            var what = session.Mode == StudioMode.Words ? "words" : "lines";
            var dialog = Dialogs.Create(songEnded ? "The song has ended" : "Keep your timing?");
            dialog.Content = Dialogs.Text(done >= all
                ? $"All {all} {what} of “{track.Title}” are timed. Save the timing?"
                : $"{done} of {all} {what} of “{track.Title}” are timed. Saving keeps only the lines you finished.");
            dialog.PrimaryButtonText = "Save timing";
            dialog.SecondaryButtonText = "Don't save";
            if (canKeep) dialog.CloseButtonText = "Keep timing";
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (SaveTiming()) afterwards();
            }
            else if (result == ContentDialogResult.Secondary) afterwards();
            else if (!canKeep) afterwards();
        }
        finally
        {
            _asking = false;
        }
    }

    // ------------------------------------------------------------------ what is shown

    private void UpdatePlayLabel()
    {
        var on = SongIsOn && _player.IsPlaying;
        PlayLabel.Icon = on ? "Pause" : "Play";
        PlayLabel.Text = on ? "Pause" : "Play";
    }

    private static Brush Themed(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// The word to press for (or the one held down, which stays the active word until it is let go), and the lines around it with
    /// their words showing which are done, which is active and which are to come.
    /// </summary>
    private void UpdateView()
    {
        if (_session is not { } session) return;
        UpdatePlayLabel();
        var words = session.Mode == StudioMode.Words;
        var unit = words ? "words" : "lines";
        ProgressText.Text = session.IsComplete && !_holding
            ? $"All {session.UnitCount} {unit} timed."
            : $"{(_holding ? session.Tapped - 1 : session.Tapped)} of {session.UnitCount} {unit}";
        SaveButton.IsEnabled = session.ToEdit().Lines is { Count: > 0 };

        // The active word: the one held down, else the next to press for.
        var active = _holding ? session.Tapped - 1 : session.Tapped;
        var allDone = !_holding && session.IsComplete;
        NowWord.FontSize = UiScale.Text(48);
        if (allDone)
        {
            NowWord.Text = "All timed";
            NowWord.Foreground = Themed("HumeTextPrimaryBrush");
            NowHint.Text = "That's the whole song. Save the timing, then watch it in the lyrics or a lyric picture. Or go back and tap again.";
        }
        else
        {
            NowWord.Text = session.Units[Math.Clamp(active, 0, session.UnitCount - 1)].Text;
            NowWord.Foreground = Themed(_holding ? "HumeTextPrimaryBrush" : "HumeTextTertiaryBrush");
            if (!_holding)
                NowHint.Text = session.Tapped > 0
                    ? $"Press the space bar when “{NowWord.Text}” begins."
                    : $"Press the space bar when “{NowWord.Text}” begins. The song is playing.";
        }

        Context.Children.Clear();
        var current = session.Units[Math.Clamp(active, 0, session.UnitCount - 1)].Line;
        for (var line = Math.Max(0, current - 1); line <= Math.Min(session.Lines.Count - 1, current + 3); line++)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, Style = (Style)Application.Current.Resources[line == current ? "HumeHeroText" : "HumeParagraphText"] };
            if (line == current) block.FontSize = UiScale.Text(28);
            if (line == current && !allDone)
            {
                // Each word in turn: done (dimmest), the active one (brightest), those to come (between).
                var first = true;
                for (var u = 0; u < session.UnitCount; u++)
                {
                    if (session.Units[u].Line != line) continue;
                    if (!first && words) block.Inlines.Add(new Run { Text = " " });
                    first = false;
                    var run = new Run { Text = session.Units[u].Text };
                    run.Foreground = u < active ? Themed("HumeTextTertiaryBrush") : u == active ? Themed("HumeTextPrimaryBrush") : Themed("HumeTextSecondaryBrush");
                    // Waiting for a press the next word is underlined; a word being held is simply lit.
                    if (u == active && !_holding) run.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    block.Inlines.Add(run);
                }
            }
            else
            {
                block.Text = session.Lines[line];
                block.Foreground = Themed(line < current || (allDone && line == current) ? "HumeTextTertiaryBrush" : "HumeTextSecondaryBrush");
            }
            Context.Children.Add(block);
        }
    }

    // ------------------------------------------------------------------ keeping

    /// <summary>Keeps the timing so far on this computer. Returns whether it was kept.</summary>
    private bool SaveTiming()
    {
        if (_track is not { } track || _session is null) return false;
        var edit = _session.ToEdit();
        var notes = App.Services.GetRequiredService<NotificationService>();
        if (edit.Lines is not { Count: > 0 })
        {
            notes.ShowInfo("Nothing to save", "No line has been timed all the way through yet.");
            return false;
        }
        if (!_lyrics.SaveTimed(track, edit))
        {
            notes.ShowError("Couldn't save the timing", "It couldn't be written to this computer.", autoHide: true);
            return false;
        }
        notes.ShowSuccess("Lyrics timed", $"“{track.Title}” now follows the timing you tapped.");
        return true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!SaveTiming()) return;
        _player.StayOnSong = false;
        _leaving = true;
        App.Services.GetRequiredService<NavigationService>().GoBack();
    }

    private async void OnUseOriginal(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        _lyrics.ClearEdits(track);
        ShowSetup();
        await LoadWordsAsync();
    }
}

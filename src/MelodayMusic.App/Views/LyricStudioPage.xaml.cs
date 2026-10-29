using MelodayMusic.Core.Lyrics;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace MelodayMusic.Views;

public sealed partial class LyricStudioPage : Page
{
    private static readonly TimeSpan UndoBack = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SeekBack = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HoldBarFills = TimeSpan.FromSeconds(4);

    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly LyricsService _lyrics = App.Services.GetRequiredService<LyricsService>();
    private Track? _track;
    private StudioSession? _session;
    private bool _holding;
    private DateTime _heldSince;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _holdTimer;
    private bool _asking, _leaving;

    public LyricStudioPage()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
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

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        _session = new StudioSession(LyricsBox.Text, ModeBox.SelectedIndex == 1 ? StudioMode.Lines : StudioMode.Words);
        if (_session.UnitCount == 0) return;
        SetupPanel.Visibility = Visibility.Collapsed;
        TimingPanel.Visibility = Visibility.Visible;
        _player.StayOnSong = true;
        StatusText.Text = string.Empty;
        if (_player.CurrentTrack?.RatingKey != track.RatingKey) await _player.PlayTracksAsync([track]);
        else
        {
            _player.Seek(TimeSpan.Zero);
            if (!_player.IsPlaying) _player.TogglePlayPause();
        }
        UpdateView();
        TapButton.Focus(FocusState.Programmatic);
    }

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

    private bool SongIsOn => _track is { } track && _player.CurrentTrack?.RatingKey == track.RatingKey;

    private bool Tap(bool hold)
    {
        if (_session is null || !Timing) return false;
        if (!SongIsOn)
        {
            StatusText.Text = "This song isn't playing. Press Play, or Start over, to play it from the start.";
            return false;
        }
        if (!_session.Tap(_player.PrecisePosition)) return false;
        if (hold) StartHolding();
        UpdateView();
        return true;
    }

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

    private void OnHoldTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_holding) return;
        var elapsed = DateTime.UtcNow - _heldSince;
        HoldBar.Value = Math.Min(1, elapsed.TotalSeconds / HoldBarFills.TotalSeconds);
        NowHint.Text = elapsed >= StudioSession.HoldThreshold
            ? "Holding. Let go when the word ends."
            : "Let go now for a short word, or keep holding for a long one.";
    }

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

    private bool _spaceDown;

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!Timing || e.Key != VirtualKey.Space) return;
        if (!e.KeyStatus.WasKeyDown && Tap(hold: true)) _spaceDown = true;
        e.Handled = true;
    }

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

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.IsPlaying)) DispatcherQueue.TryEnqueue(UpdatePlayLabel);
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

    private void OnSongEnded(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!Timing || _session is null) return;
        StopHolding();
        _spaceDown = _pointerDown = false;
        UpdatePlayLabel();
        if (_session.Tapped > 0) _ = AskThenAsync(canKeep: true, afterwards: ShowSetup, songEnded: true);
        else StatusText.Text = "The song has ended.";
    });

    private async Task AskThenAsync(bool canKeep, Action afterwards, bool songEnded = false)
    {
        if (_asking || _session is not { } session || _track is not { } track) return;
        _asking = true;
        if (SongIsOn && _player.IsPlaying) _player.Pause();
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

    private void UpdatePlayLabel()
    {
        var on = SongIsOn && _player.IsPlaying;
        PlayLabel.Icon = on ? "Pause" : "Play";
        PlayLabel.Text = on ? "Pause" : "Play";
    }

    private static Brush Themed(string key) => (Brush)Application.Current.Resources[key];

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

        var active = _holding ? session.Tapped - 1 : session.Tapped;
        var allDone = !_holding && session.IsComplete;
        NowWord.FontSize = UiScale.Text(48);
        if (allDone)
        {
            NowWord.Text = "All timed";
            NowWord.Foreground = Themed("MelodayTextPrimaryBrush");
            NowHint.Text = "Timing complete.";
        }
        else
        {
            NowWord.Text = session.Units[Math.Clamp(active, 0, session.UnitCount - 1)].Text;
            NowWord.Foreground = Themed(_holding ? "MelodayTextPrimaryBrush" : "MelodayTextTertiaryBrush");
            if (!_holding)
                NowHint.Text = session.Tapped > 0
                    ? $"Press the space bar when “{NowWord.Text}” begins."
                    : $"Press the space bar when “{NowWord.Text}” begins. The song is playing.";
        }

        Context.Children.Clear();
        var current = session.Units[Math.Clamp(active, 0, session.UnitCount - 1)].Line;
        for (var line = Math.Max(0, current - 1); line <= Math.Min(session.Lines.Count - 1, current + 3); line++)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords, Style = (Style)Application.Current.Resources[line == current ? "MelodayHeroText" : "MelodayParagraphText"] };
            if (line == current) block.FontSize = UiScale.Text(28);
            if (line == current && !allDone)
            {
                var first = true;
                for (var u = 0; u < session.UnitCount; u++)
                {
                    if (session.Units[u].Line != line) continue;
                    if (!first && words) block.Inlines.Add(new Run { Text = " " });
                    first = false;
                    var run = new Run { Text = session.Units[u].Text };
                    run.Foreground = u < active ? Themed("MelodayTextTertiaryBrush") : u == active ? Themed("MelodayTextPrimaryBrush") : Themed("MelodayTextSecondaryBrush");
                    if (u == active && !_holding) run.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    block.Inlines.Add(run);
                }
            }
            else
            {
                block.Text = session.Lines[line];
                block.Foreground = Themed(line < current || (allDone && line == current) ? "MelodayTextTertiaryBrush" : "MelodayTextSecondaryBrush");
            }
            Context.Children.Add(block);
        }
    }

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

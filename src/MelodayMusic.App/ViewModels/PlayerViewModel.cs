using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using MelodayMusic.Core.Settings;
using MelodayMusic.Helpers;
using MelodayMusic.Services;

namespace MelodayMusic.ViewModels;

public sealed partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(QueueEntry entry, int index, bool isCurrent, bool isHistory)
    {
        Entry = entry;
        Index = index;
        IsCurrent = isCurrent;
        IsHistory = isHistory;
    }

    public QueueEntry Entry { get; }
    public Track Track => Entry.Track;
    public int Index { get; }
    public bool IsCurrent { get; }
    public bool IsHistory { get; }
    public double RowOpacity => IsHistory ? 0.55 : 1.0;
    public string DurationText => Format.Duration(Track.Duration);
    public override string ToString() => $"{Track.Title}, {Track.DisplayArtist}";
}

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly PlayerService _player;
    private readonly LibraryActions _actions;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private bool _isSeeking;
    private bool _suppressQueueSync;

    public PlayerViewModel(PlayerService player, LibraryActions actions)
    {
        _player = player;
        _actions = actions;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.QueueChanged += (_, _) => RebuildQueue();
        RebuildQueue();
        UpNext.CollectionChanged += OnUpNextReordered;
        UpNextTop.CollectionChanged += OnUpNextReordered;
    }

    private void OnUpNextReordered(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_suppressQueueSync) return;
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems?[0] is not QueueItemViewModel moved) return;
        var from = _player.Queue.Items.ToList().FindIndex(x => x.Id == moved.Entry.Id);
        var to = _player.Queue.CurrentIndex + 1 + e.NewStartingIndex;
        if (from < 0 || from == to) return;
        _suppressQueueSync = true;
        _player.MoveInQueue(from, to);
        _suppressQueueSync = false;
        _dispatcher.TryEnqueue(RebuildQueue);
    }

    public ObservableCollection<QueueItemViewModel> UpNextTop { get; } = [];

    public PlayerService Player => _player;

    public ObservableCollection<QueueItemViewModel> UpNext { get; } = [];
    public ObservableCollection<QueueItemViewModel> History { get; } = [];

    public IReadOnlyList<QueueItemViewModel> UpNextPreview => UpNext.Take(8).ToList();

    private void OnFeaturedChanged()
    {
        OnPropertyChanged(nameof(HasFeatured));
        OnPropertyChanged(nameof(FeaturedArtist));
        OnPropertyChanged(nameof(FeaturedArtistThumb));
        OnPropertyChanged(nameof(FeaturedProgress));
        OnPropertyChanged(nameof(ShowLiveProgrammeHeading));
    }

    public Track? Track => _player.CurrentTrack;
    public bool HasTrack => _player.CurrentTrack is not null;
    public string Title => Track?.Title ?? "Nothing playing";
    public string Artist => Track?.DisplayArtist ?? string.Empty;
    public string Album => Track?.AlbumTitle ?? string.Empty;
    public string? ArtworkPath => Track?.DisplayThumb;
    public string? BackdropPath => Track?.Art ?? Track?.DisplayThumb;
    public bool IsPlaying => _player.IsPlaying;
    public bool IsBuffering => _player.IsBuffering;
    public string PlayPauseIcon => _player.IsPlaying ? (_player.IsLive ? "Square" : "Pause") : "Play";
    public string PlayPauseLabel => _player.IsPlaying ? (_player.IsLive ? "Stop" : "Pause") : _player.IsLive ? "Tune in" : "Play";
    public bool IsLive => _player.IsLive;
    public bool CanSeek => _player.CanSeek;
    public bool CanShuffle => HasTrack && !IsLive;
    public bool CanClearUpNext => HasUpNext && !IsLive;
    public string LiveLabel => _player.LiveStation is { } station ? $"{(_player.IsPlaying || _player.IsBuffering ? "LIVE" : "OFF AIR")} \u00B7 {station}" : string.Empty;
    public bool HasLiveProgramme => !string.IsNullOrEmpty(_player.LiveProgramme);
    public bool ShowLiveProgrammeHeading => HasLiveProgramme && !HasFeatured;
    public string LiveProgrammeLabel => !HasLiveProgramme ? string.Empty : _player.LiveBlock is not null
        ? $"Artist Takeover \u00B7 {FeaturedArtist}" : _player.LiveProgramme!;

    public bool HasFeatured => _player.LiveBlock is not null;
    public string FeaturedArtist => Track?.ArtistTitle ?? Artist;
    public string? FeaturedArtistThumb => Track?.ArtistThumb ?? Track?.DisplayThumb;
    public string FeaturedProgress => _player.LiveBlock is { } block ? $"Song {block.Position} of {block.Count} \u00B7 {_player.LiveStation}" : string.Empty;
    public bool ShowTime => HasTrack && !IsLive;
    public bool HasNext => _player.HasNext;
    public bool HasPrevious => _player.HasPrevious;
    public string? StreamDescription => _player.StreamDescription;
    public string AlbumYearText => Track is { Year: > 0 } t ? $"{t.AlbumTitle} · {t.Year}" : Album;
    public string UpNextSummary => UpNext.Count == 0 ? "Nothing queued" : $"{UpNext.Count} {(UpNext.Count == 1 ? "track" : "tracks")} · {Format.LongDuration(TimeSpan.FromMilliseconds(UpNext.Sum(i => i.Track.DurationMs)))}";
    public bool HasUpNext => UpNext.Count > 0;
    public bool HasHistory => History.Count > 0;
    public double? CurrentRating => Track?.UserRating is { } r ? r / 2.0 : null;

    public double PositionSeconds
    {
        get => _player.Position.TotalSeconds;
        set
        {
            if (Math.Abs(value - _player.Position.TotalSeconds) < 0.9) return;
            if (!_isSeeking && Math.Abs(value - DurationSeconds) < 0.001) return;
            if (_isSeeking) { SeekPreview = TimeSpan.FromSeconds(value); OnPropertyChanged(nameof(PositionText)); return; }
            _player.Seek(TimeSpan.FromSeconds(value));
        }
    }

    public TimeSpan? SeekPreview { get; private set; }

    public double DurationSeconds => Math.Max(1, _player.Duration.TotalSeconds);
    public string PositionText => Format.Duration(SeekPreview ?? _player.Position);
    public string DurationText => Format.Duration(_player.Duration);
    public string RemainingText => "-" + Format.Duration(_player.Duration - _player.Position);

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    public string VolumeIcon => _player.IsMuted || _player.Volume <= 0 ? "VolumeX" : _player.Volume < 34 ? "Volume" : _player.Volume < 67 ? "Volume1" : "Volume2";

    public bool IsShuffled
    {
        get => _player.IsShuffled;
        set { if (value != _player.IsShuffled) _player.ToggleShuffle(); }
    }

    public bool IsRepeatOn
    {
        get => _player.Repeat != RepeatMode.Off;
        set {  OnPropertyChanged(); }
    }

    public string RepeatIcon => _player.Repeat == RepeatMode.One ? "Repeat1" : "Repeat";
    public string RepeatLabel => _player.Repeat switch { RepeatMode.All => "Repeat all", RepeatMode.One => "Repeat one", _ => "Repeat off" };
    public string ShuffleLabel => _player.IsShuffled ? "Shuffle on" : "Shuffle off";

    public void BeginSeek() => _isSeeking = CanSeek;

    public void CancelSeek()
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        SeekPreview = null;
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(PositionText));
    }

    public void CommitSeek(double seconds)
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        SeekPreview = null;
        _player.Seek(TimeSpan.FromSeconds(seconds));
        OnPropertyChanged(nameof(PositionText));
    }

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private Task NextAsync() => _player.NextAsync();
    [RelayCommand] private Task PreviousAsync() => _player.PreviousAsync();
    [RelayCommand] private void Stop() => _player.Stop();
    [RelayCommand] private void ToggleShuffle() => _player.ToggleShuffle();
    [RelayCommand] private void CycleRepeat() => _player.CycleRepeat();
    [RelayCommand] private void ToggleMute() => _player.IsMuted = !_player.IsMuted;
    [RelayCommand] private void VolumeUp() => _player.Volume = Math.Min(100, _player.Volume + 5);
    [RelayCommand] private void VolumeDown() => _player.Volume = Math.Max(0, _player.Volume - 5);
    [RelayCommand] private void SeekForward() => _player.Seek(_player.Position + TimeSpan.FromSeconds(10));
    [RelayCommand] private void SeekBackward() => _player.Seek(_player.Position - TimeSpan.FromSeconds(10));
    [RelayCommand] private Task JumpToAsync(QueueItemViewModel? item) => item is null ? Task.CompletedTask : _player.JumpToAsync(item.Entry.Id);
    [RelayCommand] private void Remove(QueueItemViewModel? item) { if (item is not null) _player.RemoveFromQueue(item.Entry.Id); }
    [RelayCommand] private void ClearUpNext() => _player.ClearUpNext();
    [RelayCommand] private void ClearQueue() => _player.ClearQueue();

    [RelayCommand]
    private void OpenAlbum()
    {
        if (Track is { } t) _actions.OpenAlbum(t);
    }

    [RelayCommand]
    private void OpenArtist()
    {
        if (Track is { } t) _actions.OpenArtist(t.ArtistRatingKey, t.ArtistTitle, t.ArtistThumb);
    }

    [RelayCommand]
    private Task SonicRadioAsync() => Track is { } t ? _actions.PlaySonicRadioAsync(t) : Task.CompletedTask;

    public async Task RateCurrentAsync(double stars)
    {
        if (Track is not { } t) return;
        await _actions.RateAsync(t, stars < 0 ? -1 : stars * 2);
    }

    private void RebuildQueue()
    {
        if (_suppressQueueSync) return;
        _suppressQueueSync = true;
        var q = _player.Queue;
        UpNext.Clear();
        History.Clear();
        for (var i = q.CurrentIndex - 1; i >= Math.Max(0, q.CurrentIndex - 50); i--)
            History.Add(new QueueItemViewModel(q.Items[i], i, false, true));
        UpNextTop.Clear();
        for (var i = q.CurrentIndex + 1; i < q.Items.Count; i++)
        {
            UpNext.Add(new QueueItemViewModel(q.Items[i], i, false, false));
            if (UpNextTop.Count < 8) UpNextTop.Add(new QueueItemViewModel(q.Items[i], i, false, false));
        }
        _suppressQueueSync = false;
        OnPropertyChanged(nameof(UpNextSummary));
        OnPropertyChanged(nameof(UpNextPreview));
        OnPropertyChanged(nameof(HasUpNext));
        OnPropertyChanged(nameof(CanClearUpNext));
        OnPropertyChanged(nameof(CanShuffle));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsShuffled));
        OnPropertyChanged(nameof(ShuffleLabel));
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerService.CurrentTrack):
                CancelSeek();
                OnPropertyChanged(nameof(Track));
                OnPropertyChanged(nameof(HasTrack));
                OnPropertyChanged(nameof(CanShuffle));
                OnPropertyChanged(nameof(CanSeek));
                OnPropertyChanged(nameof(ShowTime));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Artist));
                OnPropertyChanged(nameof(Album));
                OnPropertyChanged(nameof(AlbumYearText));
                OnPropertyChanged(nameof(ArtworkPath));
                OnPropertyChanged(nameof(BackdropPath));
                OnPropertyChanged(nameof(CurrentRating));
                OnFeaturedChanged();
                RebuildQueue();
                break;
            case nameof(PlayerService.LiveBlock):
                OnFeaturedChanged();
                OnPropertyChanged(nameof(LiveProgrammeLabel));
                break;
            case nameof(PlayerService.LiveProgramme):
                OnPropertyChanged(nameof(HasLiveProgramme));
                OnPropertyChanged(nameof(ShowLiveProgrammeHeading));
                OnPropertyChanged(nameof(LiveProgrammeLabel));
                break;
            case nameof(PlayerService.IsPlaying):
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(PlayPauseIcon));
                OnPropertyChanged(nameof(PlayPauseLabel));
                OnPropertyChanged(nameof(LiveLabel));
                break;
            case nameof(PlayerService.IsBuffering):
                OnPropertyChanged(nameof(IsBuffering));
                OnPropertyChanged(nameof(LiveLabel));
                break;
            case nameof(PlayerService.Position):
                if (!_isSeeking)
                {
                    OnPropertyChanged(nameof(PositionSeconds));
                    OnPropertyChanged(nameof(PositionText));
                }
                OnPropertyChanged(nameof(RemainingText));
                break;
            case nameof(PlayerService.Duration):
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(RemainingText));
                break;
            case nameof(PlayerService.Volume):
            case nameof(PlayerService.IsMuted):
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(VolumeIcon));
                break;
            case nameof(PlayerService.IsShuffled):
                OnPropertyChanged(nameof(IsShuffled));
                OnPropertyChanged(nameof(ShuffleLabel));
                break;
            case nameof(PlayerService.Repeat):
                OnPropertyChanged(nameof(IsRepeatOn));
                OnPropertyChanged(nameof(RepeatIcon));
                OnPropertyChanged(nameof(RepeatLabel));
                break;
            case nameof(PlayerService.HasNext):
            case nameof(PlayerService.HasPrevious):
                OnPropertyChanged(nameof(HasNext));
                OnPropertyChanged(nameof(HasPrevious));
                break;
            case nameof(PlayerService.LiveStation):
                OnPropertyChanged(nameof(PlayPauseIcon));
                OnPropertyChanged(nameof(PlayPauseLabel));
                OnPropertyChanged(nameof(IsLive));
                OnPropertyChanged(nameof(CanShuffle));
                OnPropertyChanged(nameof(CanSeek));
                OnPropertyChanged(nameof(LiveLabel));
                OnPropertyChanged(nameof(ShowTime));
                OnPropertyChanged(nameof(CanClearUpNext));
                OnFeaturedChanged();
                break;
            case nameof(PlayerService.StreamDescription):
                OnPropertyChanged(nameof(StreamDescription));
                break;
        }
    }
}

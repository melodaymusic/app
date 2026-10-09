using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.ViewModels;

/// <summary>The Meloday page: the mix for this part of the day, its cover, name, description and tracks.</summary>
public sealed partial class MelodayViewModel : ViewModelBase
{
    private readonly MelodayService _meloday;
    private readonly DispatcherQueue _dispatcher;

    public MelodayViewModel(PlexSession session, MelodayService meloday, ILogger<MelodayViewModel> log) : base(session, log)
    {
        _meloday = meloday;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _meloday.Changed += (_, _) => _dispatcher.TryEnqueue(Show);
        _meloday.PlexSyncChanged += (_, _) => _dispatcher.TryEnqueue(ShowPlexSync);
        ShowPlexSync();
    }

    private bool _showingSync;

    /// <summary>Whether this Meloday is kept on the Plex server (one playlist, updated each time).</summary>
    [ObservableProperty] public partial bool KeepOnPlex { get; set; }
    /// <summary>"Kept on Plex · updated 2:30 PM", or why it couldn't be.</summary>
    [ObservableProperty] public partial string PlexStatus { get; private set; } = string.Empty;

    partial void OnKeepOnPlexChanged(bool value)
    {
        if (!_showingSync) _ = _meloday.SetKeepOnPlexAsync(value);
    }

    private void ShowPlexSync()
    {
        _showingSync = true;
        KeepOnPlex = _meloday.KeepOnPlex;
        PlexStatus = _meloday.PlexStatus;
        _showingSync = false;
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>The page header's item: Meloday, titled with the current mix's name.</summary>
    [ObservableProperty] public partial Station Item { get; private set; } = MelodayService.Station;
    [ObservableProperty] public partial string? Cover { get; private set; }
    /// <summary>The part of the day and the mix's name, for the live cover.</summary>
    [ObservableProperty] public partial string Period { get; private set; } = MelodayPeriods.At(DateTime.Now).Name;
    [ObservableProperty] public partial string? CoverTitle { get; private set; }
    [ObservableProperty] public partial string Meta { get; private set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; private set; } = string.Empty;

    public async Task LoadAsync(bool fresh = false)
    {
        IsEmpty = false;
        ErrorMessage = null;
        if (fresh || _meloday.Current is null || _meloday.IsMaking)
        {
            var period = MelodayPeriods.At(DateTime.Now);
            IsLoading = true;
            if (Tracks.Count == 0 || fresh)
            {
                Tracks.Clear();
                Item = MelodayService.Station;
                Cover = null;
                Period = period.Name;
                CoverTitle = null;
                Meta = period.Name;
                Description = $"Making your Meloday for {period.Phrase}: the music you play at this time, what sounds like it, and a little more.";
            }
        }
        var mix = await _meloday.GetAsync(fresh);
        Show();
        if (mix is null) ErrorMessage = "Meloday couldn't make a mix just now. Check your connection, then try again.";
    }

    /// <summary>The meta line (part of the day, how many tracks, how long) for the tracks the page holds now.</summary>
    public void ShowCount()
    {
        if (_meloday.Current is not { } mix) return;
        var length = TimeSpan.FromMilliseconds(Tracks.Sum(t => t.DurationMs));
        var duration = length.TotalHours >= 1 ? $"{(int)length.TotalHours} hr {length.Minutes} min" : $"{length.Minutes} min";
        Meta = $"{mix.Period.Name} · {Tracks.Count} tracks · {duration}";
    }

    private void Show()
    {
        if (_meloday.IsMaking) return;
        IsLoading = false;
        if (_meloday.Current is not { } mix) return;
        Item = new Station
        {
            RatingKey = MelodayService.Station.RatingKey,
            Title = mix.Title,
            StationKind = StationKind.Meloday,
            Colour = MelodayService.Station.Colour,
            Summary = MelodayService.Station.Summary,
        };
        Cover = mix.CoverPath;
        Period = mix.Period.Name;
        CoverTitle = mix.Title;
        Description = mix.Description;
        Tracks.Clear();
        foreach (var t in mix.Tracks) Tracks.Add(t);
        ShowCount();
        IsEmpty = Tracks.Count == 0;
    }
}

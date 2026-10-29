using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.ViewModels;

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

    [ObservableProperty] public partial bool KeepOnPlex { get; set; }
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

    [ObservableProperty] public partial Station Item { get; private set; } = MelodayService.Station;
    [ObservableProperty] public partial string? Cover { get; private set; }
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

    public void ShowCount()
    {
        if (_meloday.Current is not { } mix) return;
        var length = TimeSpan.FromMilliseconds(Tracks.Sum(t => t.DurationMs));
        var duration = length.TotalHours >= 1 ? $"{(int)length.TotalHours} hr {length.Minutes} min" : $"{length.Minutes} min";
        Meta = $"{mix.Period.Name} · {Tracks.Count} tracks · {duration}";
        var attribution = DescriptionParts(mix.Description).Metadata;
        if (attribution.Length > 0) Meta += " · " + attribution;
    }

    private static (string Body, string Metadata) DescriptionParts(string description)
    {
        var normalized = description.Replace("\r\n", "\n", StringComparison.Ordinal);
        var split = normalized.LastIndexOf("\n\nMade for ", StringComparison.Ordinal);
        return split < 0 ? (description, string.Empty) : (normalized[..split].TrimEnd(), normalized[(split + 2)..].Trim());
    }

    private void Show()
    {
        if (_meloday.IsMaking) return;
        IsLoading = false;
        if (_meloday.Current is not { } mix) return;
        Item = new Station
        {
            RatingKey = MelodayService.Station.RatingKey,
            Title = mix.Title.StartsWith(MelodayNaming.Prefix, StringComparison.OrdinalIgnoreCase) ? mix.Title[MelodayNaming.Prefix.Length..] : mix.Title,
            StationKind = StationKind.Meloday,
            Colour = MelodayService.Station.Colour,
            Summary = MelodayService.Station.Summary,
        };
        Cover = mix.CoverPath;
        Period = mix.Period.Name;
        CoverTitle = mix.Title;
        Description = DescriptionParts(mix.Description).Body;
        Tracks.Clear();
        foreach (var t in mix.Tracks) Tracks.Add(t);
        ShowCount();
        IsEmpty = Tracks.Count == 0;
    }
}

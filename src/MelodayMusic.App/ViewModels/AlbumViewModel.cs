using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed class AlbumTrackRow
{
    public required Track Track { get; init; }
    public string? DiscHeader { get; init; }
    public bool ShowTrackArtist { get; init; }
    public bool HasDiscHeader => DiscHeader is not null;
}

public sealed partial class AlbumViewModel : ViewModelBase
{
    public AlbumViewModel(PlexSession session, ILogger<AlbumViewModel> log) : base(session, log) { }

    [ObservableProperty] public partial Album? Album { get; private set; }
    public ObservableCollection<AlbumTrackRow> Tracks { get; } = [];
    public ObservableCollection<Album> MoreByArtist { get; } = [];
    public ObservableCollection<Album> SimilarAlbums { get; } = [];
    [ObservableProperty] public partial string MetaText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string FormatText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string TagsText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ReleaseText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string FooterText { get; private set; } = string.Empty;

    public IReadOnlyList<Track> TrackList => Tracks.Select(r => r.Track).ToList();

    public async Task RefreshAlbumAsync()
    {
        if (Album is not { } current || Session.Client is not { } client) return;
        try
        {
            if (await client.GetAlbumAsync(current.RatingKey) is { } fresh) Album = fresh;
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Album not refreshed: {Message}", ex.Message);
        }
    }

    public async Task LoadAsync(Album seed)
    {
        Album = seed;
        await RunAsync(async () =>
        {
            var client = Client;
            var albumTask = client.GetAlbumAsync(seed.RatingKey);
            var tracksTask = client.GetAlbumTracksAsync(seed.RatingKey);
            await Task.WhenAll(albumTask, tracksTask);
            var album = albumTask.Result ?? seed;
            Album = album;
            var tracks = tracksTask.Result;

            var discs = tracks.Select(t => t.DiscNumber ?? 1).Distinct().Count();
            var albumArtist = album.ArtistTitle;
            Tracks.Clear();
            int? lastDisc = null;
            foreach (var t in tracks.OrderBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber ?? int.MaxValue))
            {
                var disc = t.DiscNumber ?? 1;
                Tracks.Add(new AlbumTrackRow
                {
                    Track = t,
                    DiscHeader = discs > 1 && disc != lastDisc ? $"Disc {disc}" : null,
                    ShowTrackArtist = !string.IsNullOrEmpty(t.TrackArtist) && !string.Equals(t.TrackArtist, albumArtist, StringComparison.OrdinalIgnoreCase),
                });
                lastDisc = disc;
            }

            var total = TimeSpan.FromMilliseconds(tracks.Sum(t => t.DurationMs));
            var meta = new List<string>();
            if (album.Genres.Count > 0) meta.Add(album.Genres[0]);
            if (album.Year is > 0) meta.Add(album.Year.Value.ToString());
            MetaText = string.Join(" · ", meta);

            ReleaseText = album.ReleaseDate is { } released ? $"Released {released:d MMMM yyyy}" : string.Empty;
            var footer = $"{Format.Count(tracks.Count, "track", "tracks")}, {Format.LongDuration(total)}";
            if (album.ViewCount > 0) footer += $" · {Format.Plays(album.ViewCount)}";
            FooterText = footer;
            var fmt = tracks.FirstOrDefault()?.Media?.Describe() ?? string.Empty;
            FormatText = string.IsNullOrEmpty(album.Studio) ? fmt : string.IsNullOrEmpty(fmt) ? album.Studio! : $"{album.Studio} · {fmt}";
            TagsText = string.Join("  ·  ", album.Genres.Concat(album.Styles).Concat(album.Moods).Distinct().Take(8));
            IsEmpty = Tracks.Count == 0;
        });

        _ = LoadRelatedAsync(seed);
    }

    private async Task LoadRelatedAsync(Album seed)
    {
        try
        {
            var client = Client;
            var artistKey = Album?.ArtistRatingKey ?? seed.ArtistRatingKey;
            var moreTask = artistKey is null ? Task.FromResult<IReadOnlyList<Album>>([]) : client.GetArtistAlbumsAsync(artistKey);
            var similarTask = App.Services.GetRequiredService<Services.MusicIntelligence>().SimilarAsync<Album>(seed.RatingKey, 16, 0.3);
            try { await Task.WhenAll(moreTask, similarTask); } catch (PlexException) { }

            MoreByArtist.Clear();
            if (moreTask.IsCompletedSuccessfully)
                foreach (var a in moreTask.Result.Where(a => a.RatingKey != seed.RatingKey).OrderByDescending(a => a.Year).Take(16)) MoreByArtist.Add(a);
            SimilarAlbums.Clear();
            if (similarTask.IsCompletedSuccessfully)
                foreach (var a in similarTask.Result.Where(a => a.RatingKey != seed.RatingKey).Take(16)) SimilarAlbums.Add(a);
        }
        catch (PlexException ex)
        {
            Log.LogDebug("Related albums unavailable: {Message}", ex.Message);
        }
    }
}

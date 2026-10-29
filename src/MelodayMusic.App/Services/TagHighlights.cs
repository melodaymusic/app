using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class TagHighlights
{
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(183);
    public static readonly TimeSpan QuietFor = TimeSpan.FromDays(90);
    public const int MaxBillboards = 5;

    private readonly PlexSession _session;
    private readonly ILogger<TagHighlights> _log;

    public TagHighlights(PlexSession session, ILogger<TagHighlights> log)
    {
        _session = session;
        _log = log;
    }

    public async Task<IReadOnlyList<BoardItem>> GetAsync(TagItem tag)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        try
        {
            var albumFilter = new KeyValuePair<string, string?>(tag.TagKind == TagKind.Mood ? "track.mood" : PlexServerClient.TagField(tag.TagKind), tag.FilterValue);
            var artistFilter = new KeyValuePair<string, string?>(PlexServerClient.TagField(tag.TagKind), tag.FilterValue);
            var newest = client.GetAlbumsAsync(section.Key, 0, 6, "addedAt:desc", default, albumFilter);
            var top = client.GetAlbumsAsync(section.Key, 0, 12, "viewCount:desc", default, albumFilter);
            var artists = client.GetArtistsAsync(section.Key, 0, 6, "viewCount:desc", default, artistFilter);
            var unplayed = client.GetAlbumsAsync(section.Key, 0, 30, "random", default, albumFilter, new KeyValuePair<string, string?>("viewCount", "0"));
            await Task.WhenAll(newest, top, artists, unplayed);
            var names = tag.IsFamily ? tag.Members.Select(m => m.Title).ToList() : [tag.Title];
            return Pick(tag.Title, names, DateTimeOffset.Now, newest.Result.Items, top.Result.Items, artists.Result.Items, unplayed.Result.Items);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogDebug("No highlights for {Tag}: {Message}", tag.Title, ex.Message);
            return [];
        }
    }

    /// <param name="genres">The tags this page covers: the genre itself, or every genre of a family such as R&amp;B &amp; Soul.</param>
    public static IReadOnlyList<BoardItem> Pick(string tag, IReadOnlyCollection<string> genres, DateTimeOffset now, IReadOnlyList<Album> newest, IReadOnlyList<Album> top,
        IReadOnlyList<Artist> artists, IReadOnlyList<Album> unplayed)
    {
        var here = new HashSet<string>(genres, StringComparer.OrdinalIgnoreCase);
        // An artist counts for a page by any of their tags, but is shown by the one that belongs here: a pop artist with one R&B tag
        // reads as R&B on the R&B page, and one whose main tag belongs here goes first.
        string? TagHere(Artist a) => a.Genres.FirstOrDefault(here.Contains);

        var boards = new List<BoardItem>();
        var used = new HashSet<string>();
        bool Take(MediaItem item) => !string.IsNullOrEmpty(item.Art) && used.Add(item.RatingKey);

        if (newest.FirstOrDefault(a => a.AddedAt is { } added && now - added <= NewFor && !string.IsNullOrEmpty(a.Art)) is { } fresh && Take(fresh))
            boards.Add(new BoardItem
            {
                Item = fresh, CoverPath = fresh.Thumb, Eyebrow = $"New in {tag}", Title = fresh.Title, Subtitle = fresh.ArtistTitle,
                Detail = $"Added {Helpers.Format.Ago(fresh.AddedAt!.Value).ToLowerInvariant()}", ArtPath = fresh.Art,
            });

        if (top.FirstOrDefault(a => a.ViewCount >= 3 && !string.IsNullOrEmpty(a.Art) && !used.Contains(a.RatingKey)) is { } favourite && Take(favourite))
            boards.Add(new BoardItem
            {
                Item = favourite, CoverPath = favourite.Thumb, Eyebrow = $"Your top {tag} album", Title = favourite.Title, Subtitle = favourite.ArtistTitle,
                Detail = Helpers.Format.Plays(favourite.ViewCount), ArtPath = favourite.Art,
            });

        var played = artists.Where(a => a.ViewCount >= 5 && !string.IsNullOrEmpty(a.Art))
            .OrderByDescending(a => a.Genres.Count > 0 && here.Contains(a.Genres[0]));
        if (played.FirstOrDefault() is { } artist && Take(artist))
            boards.Add(new BoardItem
            {
                Item = artist, Eyebrow = $"Your {tag} artist", Title = artist.Title,
                Subtitle = TagHere(artist) ?? tag, Detail = Helpers.Format.Plays(artist.ViewCount), ArtPath = artist.Art,
            });

        if (top.FirstOrDefault(a => a.ViewCount >= 3 && a.LastViewedAt is { } last && now - last >= QuietFor && !used.Contains(a.RatingKey) && !string.IsNullOrEmpty(a.Art)) is { } quiet && Take(quiet))
            boards.Add(new BoardItem
            {
                Item = quiet, CoverPath = quiet.Thumb, Eyebrow = "Rediscover", Title = quiet.Title, Subtitle = quiet.ArtistTitle,
                Detail = $"Last played {Helpers.Format.Ago(quiet.LastViewedAt!.Value).ToLowerInvariant()}", ArtPath = quiet.Art,
            });

        var known = artists.Where(a => a.ViewCount > 0).Select(a => a.RatingKey).ToHashSet();
        var suggestion = unplayed.FirstOrDefault(a => a.ArtistRatingKey is { } k && known.Contains(k) && !string.IsNullOrEmpty(a.Art) && !used.Contains(a.RatingKey))
                         ?? unplayed.FirstOrDefault(a => !string.IsNullOrEmpty(a.Art) && !used.Contains(a.RatingKey));
        if (suggestion is not null && Take(suggestion))
            boards.Add(new BoardItem
            {
                Item = suggestion, CoverPath = suggestion.Thumb, Eyebrow = "Not played yet", Title = suggestion.Title, Subtitle = suggestion.ArtistTitle,
                Detail = suggestion.Year?.ToString(), ArtPath = suggestion.Art,
            });

        return boards.Take(MaxBillboards).ToList();
    }
}

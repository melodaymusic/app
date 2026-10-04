using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// The billboards at the top of a Browse hub (Pop, Chill &amp; Calm...): what's worth putting first
/// from the listener's own library in that corner of it. Each is shown only when it's true: "New in
/// Pop" only when something was added lately, "Your top album" only when one has really been played,
/// "Rediscover" only for a favourite gone quiet, and an unplayed album as a suggestion. A hub with
/// nothing to say shows no billboards at all.
/// </summary>
public sealed class TagHighlights
{
    /// <summary>"New" means added within this long.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(183);
    /// <summary>A favourite unplayed for this long is worth rediscovering.</summary>
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
            // The same filters as the tag's own pages (a family's matches any of its members).
            var albumFilter = new KeyValuePair<string, string?>(tag.TagKind == TagKind.Mood ? "track.mood" : PlexServerClient.TagField(tag.TagKind), tag.FilterValue);
            var artistFilter = new KeyValuePair<string, string?>(PlexServerClient.TagField(tag.TagKind), tag.FilterValue);
            var newest = client.GetAlbumsAsync(section.Key, 0, 6, "addedAt:desc", default, albumFilter);
            var top = client.GetAlbumsAsync(section.Key, 0, 12, "viewCount:desc", default, albumFilter);
            var artists = client.GetArtistsAsync(section.Key, 0, 6, "viewCount:desc", default, artistFilter);
            var unplayed = client.GetAlbumsAsync(section.Key, 0, 30, "random", default, albumFilter, new KeyValuePair<string, string?>("viewCount", "0"));
            await Task.WhenAll(newest, top, artists, unplayed);
            return Pick(tag.Title, DateTimeOffset.Now, newest.Result.Items, top.Result.Items, artists.Result.Items, unplayed.Result.Items);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogDebug("No highlights for {Tag}: {Message}", tag.Title, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Chooses the billboards (testable on its own): each only when its condition holds, none twice,
    /// at most <see cref="MaxBillboards"/>, all with a backdrop to show.
    /// </summary>
    public static IReadOnlyList<BoardItem> Pick(string tag, DateTimeOffset now, IReadOnlyList<Album> newest, IReadOnlyList<Album> top,
        IReadOnlyList<Artist> artists, IReadOnlyList<Album> unplayed)
    {
        var boards = new List<BoardItem>();
        var used = new HashSet<string>();
        bool Take(MediaItem item) => !string.IsNullOrEmpty(item.Art) && used.Add(item.RatingKey);

        // Lately added to this corner of the library.
        if (newest.FirstOrDefault(a => a.AddedAt is { } added && now - added <= NewFor && !string.IsNullOrEmpty(a.Art)) is { } fresh && Take(fresh))
            boards.Add(new BoardItem
            {
                Item = fresh, CoverPath = fresh.Thumb, Eyebrow = $"New in {tag}", Title = fresh.Title, Subtitle = fresh.ArtistTitle,
                Detail = $"Added {Helpers.Format.Ago(fresh.AddedAt!.Value).ToLowerInvariant()}", ArtPath = fresh.Art,
            });

        // The album played most, when it truly has been.
        if (top.FirstOrDefault(a => a.ViewCount >= 3 && !string.IsNullOrEmpty(a.Art) && !used.Contains(a.RatingKey)) is { } favourite && Take(favourite))
            boards.Add(new BoardItem
            {
                Item = favourite, CoverPath = favourite.Thumb, Eyebrow = $"Your top {tag} album", Title = favourite.Title, Subtitle = favourite.ArtistTitle,
                Detail = Helpers.Format.Plays(favourite.ViewCount), ArtPath = favourite.Art,
            });

        // The artist played most.
        if (artists.FirstOrDefault(a => a.ViewCount >= 5 && !string.IsNullOrEmpty(a.Art)) is { } artist && Take(artist))
            boards.Add(new BoardItem
            {
                Item = artist, Eyebrow = $"Your {tag} artist", Title = artist.Title,
                Subtitle = artist.Genres.Count > 0 ? artist.Genres[0] : null, Detail = Helpers.Format.Plays(artist.ViewCount), ArtPath = artist.Art,
            });

        // A favourite gone quiet.
        if (top.FirstOrDefault(a => a.ViewCount >= 3 && a.LastViewedAt is { } last && now - last >= QuietFor && !used.Contains(a.RatingKey) && !string.IsNullOrEmpty(a.Art)) is { } quiet && Take(quiet))
            boards.Add(new BoardItem
            {
                Item = quiet, CoverPath = quiet.Thumb, Eyebrow = "Rediscover", Title = quiet.Title, Subtitle = quiet.ArtistTitle,
                Detail = $"Last played {Helpers.Format.Ago(quiet.LastViewedAt!.Value).ToLowerInvariant()}", ArtPath = quiet.Art,
            });

        // Not played yet: by an artist already loved here if there is one.
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

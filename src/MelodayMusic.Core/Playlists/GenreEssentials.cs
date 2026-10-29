using System.Globalization;
using System.Text;
using System.Text.Json;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playlists;

public sealed record GenreCover(string Genre, string File)
{
    public string Slug => GenreEssentials.Slug(Genre);
}

public static class GenreEssentials
{
    public const int Length = 25;
    public const int Smallest = 12;
    public const int MaxPerArtist = 2;
    public const int Count = 12;
    public const string IdPrefix = "genre-";

    private static IReadOnlyList<GenreCover> _catalogue = [];

    public static IReadOnlyList<GenreCover> Catalogue => _catalogue;

    public static IReadOnlyList<GenreCover> Load(string json)
    {
        using var document = JsonDocument.Parse(json);
        var covers = document.RootElement.EnumerateArray()
            .Select(e => new GenreCover(e.GetProperty("genre").GetString() ?? string.Empty, e.GetProperty("file").GetString() ?? string.Empty))
            .Where(c => c.Genre.Length > 0 && c.File.Length > 0)
            .ToList();
        _catalogue = covers;
        return covers;
    }

    public static string Normalize(string genre)
    {
        var text = genre.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(text.Length);
        foreach (var c in text)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        var spaced = $" {plain} ".Replace("&", " and ").Replace("’n’", " and ").Replace("'n'", " and ").Replace(" n ", " and ");
        return new string(spaced.Where(char.IsLetterOrDigit).ToArray());
    }

    public static string Slug(string genre)
    {
        var text = genre.Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (c == '&') slug.Append("-and-");
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-').Replace("--", "-");
    }

    public static IReadOnlyList<(GenreCover Cover, TagItem Tag)> Match(IEnumerable<GenreCover> catalogue, IEnumerable<TagItem> libraryGenres)
    {
        var covers = catalogue.GroupBy(c => Normalize(c.Genre)).ToDictionary(g => g.Key, g => g.ToList());
        var matched = new List<(GenreCover, TagItem)>();
        var seen = new HashSet<string>();
        foreach (var tag in libraryGenres)
        {
            var key = Normalize(tag.Title);
            if (key.Length == 0 || string.IsNullOrWhiteSpace(tag.FilterValue) || !covers.TryGetValue(key, out var options) || !seen.Add(key)) continue;
            matched.Add((options[0], tag));
        }
        return matched;
    }

    public static IReadOnlyList<Track> Pick(IEnumerable<Track> byPopularity, int length = Length)
    {
        var songs = new SongSet();
        var perArtist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var picked = new List<Track>();
        foreach (var track in byPopularity)
        {
            if (picked.Count >= length) break;
            if (track.UserRating is > 0 and <= 2) continue;
            var artist = MelodayText.NormalizeArtist(MelodayText.TrackArtist(track));
            if (perArtist.GetValueOrDefault(artist) >= MaxPerArtist || !songs.Add(track)) continue;
            perArtist[artist] = perArtist.GetValueOrDefault(artist) + 1;
            picked.Add(track);
        }
        return picked.Count < Smallest ? [] : picked;
    }

    public static PlaylistRecipe Recipe(GenreCover cover) => new(IdPrefix + cover.Slug, cover.Genre + " Essentials",
        $"The best-known {cover.Genre} songs in your library.", [], [], null, PlaylistShelf.Genre);

    public static PlaylistRecipe? RecipeOf(string id)
    {
        if (!id.StartsWith(IdPrefix, StringComparison.Ordinal)) return null;
        var slug = id[IdPrefix.Length..];
        return _catalogue.FirstOrDefault(c => c.Slug == slug) is { } cover ? Recipe(cover) : null;
    }

    public static GenreCover? CoverOf(string id) =>
        id.StartsWith(IdPrefix, StringComparison.Ordinal) ? _catalogue.FirstOrDefault(c => c.Slug == id[IdPrefix.Length..]) : null;
}

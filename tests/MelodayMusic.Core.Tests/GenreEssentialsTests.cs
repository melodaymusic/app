using MelodayMusic.Core.Covers;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;

namespace MelodayMusic.Core.Tests;

public class GenreEssentialsTests
{
    private static Track T(string key, string title, string artist, double? rating = null) =>
        new() { RatingKey = key, Title = title, ArtistTitle = artist, AlbumTitle = "Album " + key, DurationMs = 200_000, UserRating = rating };

    [Theory]
    [InlineData("Drum ’n’ Bass", "Drum & Bass")]
    [InlineData("Drum 'n' Bass", "drum and bass")]
    [InlineData("Reggaetón", "Reggaeton")]
    [InlineData("Hip-Hop en Español", "Hip Hop en Espanol")]
    [InlineData("Indie R&B", "Indie R and B")]
    [InlineData("Synth-Pop", "Synth Pop")]
    public void Spellings_of_a_genre_meet(string cover, string library) =>
        Assert.Equal(GenreEssentials.Normalize(cover), GenreEssentials.Normalize(library));

    [Fact]
    public void Different_genres_stay_apart() =>
        Assert.NotEqual(GenreEssentials.Normalize("Indie Pop"), GenreEssentials.Normalize("Indie Rock"));

    [Fact]
    public void Only_library_genres_with_a_cover_are_matched_each_once()
    {
        var catalogue = new[] { new GenreCover("Indie Rock", "indie-rock.jpg"), new GenreCover("College Rock", "college-rock-1.jpg"),
            new GenreCover("College Rock", "college-rock-2.jpg"), new GenreCover("Trap", "trap.jpg") };
        var library = new[]
        {
            new TagItem { RatingKey = "1", Title = "Indie Rock", FilterValue = "11", TagKind = TagKind.Genre },
            new TagItem { RatingKey = "2", Title = "indie rock", FilterValue = "12", TagKind = TagKind.Genre },
            new TagItem { RatingKey = "3", Title = "College Rock", FilterValue = "13", TagKind = TagKind.Genre },
            new TagItem { RatingKey = "4", Title = "Polka", FilterValue = "14", TagKind = TagKind.Genre },
        };

        var matched = GenreEssentials.Match(catalogue, library);

        Assert.Equal(["Indie Rock", "College Rock"], matched.Select(m => m.Cover.Genre));
        Assert.Equal("college-rock-1.jpg", matched[1].Cover.File);
        Assert.Equal("college-rock", matched[1].Cover.Slug);
    }

    [Fact]
    public void The_most_popular_songs_each_once_two_per_artist_and_none_disliked()
    {
        var byPopularity = new List<Track>
        {
            T("1", "Song A", "Artist 1"), T("2", "Song A", "Artist 1"),
            T("3", "Song B", "Artist 1"), T("4", "Song C", "Artist 1"),
            T("5", "Song D", "Artist 2", rating: 2),
        };
        byPopularity.AddRange(Enumerable.Range(10, 30).Select(i => T(i.ToString(), "Song " + i, "Artist " + i)));

        var picked = GenreEssentials.Pick(byPopularity);

        Assert.Equal(GenreEssentials.Length, picked.Count);
        Assert.Equal(["1", "3", "10"], picked.Take(3).Select(t => t.RatingKey));
        Assert.DoesNotContain(picked, t => t.RatingKey is "2" or "4" or "5");
        Assert.Empty(GenreEssentials.Pick(byPopularity.Take(5)));
    }

    [Fact]
    public void A_genre_playlist_is_a_made_playlist_of_its_own_kind()
    {
        GenreEssentials.Load("""[{"genre":"Indie Rock","file":"indie-rock.jpg"}]""");
        var recipe = MadePlaylists.RecipeOf("made:genre-indie-rock")!;
        Assert.Equal("Indie Rock Essentials", recipe.Title);
        Assert.Equal(PlaylistShelf.Genre, recipe.Shelf);
        Assert.Null(MadePlaylists.RecipeOf("made:genre-polka"));
        Assert.Equal("feel-good", MadePlaylists.RecipeOf("made:feel-good")!.Id);
    }

    [Theory]
    [InlineData(0x2A2930u, 0xFFFFFFu)]
    [InlineData(0xE14687u, 0xFFFFFFu)]
    [InlineData(0xFFB4BEu, 0xFFFFFFu)]
    [InlineData(0xF2E6FCu, 0x000000u)]
    [InlineData(0xFFFFFFu, 0x000000u)]
    public void The_symbol_is_white_unless_the_ground_is_extremely_light(uint ground, uint ink) =>
        Assert.Equal(ink, CoverInk.Symbol(ground));
}

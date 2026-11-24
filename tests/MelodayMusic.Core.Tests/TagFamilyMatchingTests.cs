using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

[Collection(nameof(TagFamilyMatchingTests))]
public class TagFamilyMatchingTests : IDisposable
{
    public TagFamilyMatchingTests() => TagFamilies.UseOverrides(null);
    public void Dispose() => TagFamilies.UseOverrides(null);

    [Theory]
    // The broad ones.
    [InlineData("Pop", "Pop")]
    [InlineData("Hip-Hop/Rap", "Hip-Hop")]
    [InlineData("R&B", "R&B & Soul")]
    [InlineData("Dance/Electronic", "Electronic")]
    [InlineData("Alternative/Indie", "Alternative")]
    [InlineData("Singer/Songwriter", "Folk")]
    [InlineData("Children's Music", "Kids & Family")]
    // A phrase made for the genre beats a single word in it.
    [InlineData("UK Garage", "Electronic")]
    [InlineData("Melbourne Bounce", "Electronic")]
    [InlineData("Happy Hardcore", "Electronic")]
    [InlineData("Tropical House", "Electronic")]
    [InlineData("Afro House", "Electronic")]
    [InlineData("Dance Pop", "Pop")]
    [InlineData("Lo-Fi Hip Hop", "Hip-Hop")]
    [InlineData("Lo-Fi Beats", "Hip-Hop")]
    [InlineData("Western Swing", "Country")]
    [InlineData("Rhythm and Blues", "R&B & Soul")]
    [InlineData("Reggae Fusion", "Reggae")]
    [InlineData("Jazz Funk", "Jazz")]
    [InlineData("Emo Rap", "Hip-Hop")]
    // The last word says what it is.
    [InlineData("Psychedelic Trance", "Electronic")]
    [InlineData("Psychedelic Rock", "Rock")]
    [InlineData("Garage Rock", "Rock")]
    [InlineData("Hardcore Punk", "Rock")]
    [InlineData("Soul Jazz", "Jazz")]
    [InlineData("Pop Rock", "Rock")]
    [InlineData("Pop Rap", "Hip-Hop")]
    [InlineData("Blues Rock", "Rock")]
    [InlineData("Country Blues", "Blues")]
    [InlineData("Trap Metal", "Metal")]
    [InlineData("Vocal Jazz", "Jazz")]
    [InlineData("Avant-Garde Jazz", "Jazz")]
    [InlineData("Synth-Pop", "Pop")]
    [InlineData("Trip-Hop", "Electronic")]
    // A word of place, faith or occasion sets the family.
    [InlineData("Latin Trap", "Latin")]
    [InlineData("Latin Pop", "Latin")]
    [InlineData("Christmas Pop", "Holiday")]
    [InlineData("Christian Rock", "Gospel & Christian")]
    [InlineData("Christian Hip Hop", "Gospel & Christian")]
    [InlineData("Indie Rock", "Alternative")]
    [InlineData("Indie Pop", "Alternative")]
    [InlineData("Post-Punk", "Alternative")]
    [InlineData("Alt-Country", "Country")]
    [InlineData("Alternative Metal", "Metal")]
    // Parents that were missing.
    [InlineData("Afrobeats", "African")]
    [InlineData("Amapiano", "African")]
    [InlineData("Bollywood", "South Asian")]
    [InlineData("Punjabi", "South Asian")]
    [InlineData("Anime", "Soundtrack")]
    [InlineData("Broadway", "Soundtrack")]
    [InlineData("Regional Mexican", "Latin")]
    [InlineData("Baile Funk", "World")]
    [InlineData("K-Pop", "Pop")]
    [InlineData("J-Rock", "Rock")]
    [InlineData("Metalcore", "Metal")]
    [InlineData("Mathcore", "Metal")]
    [InlineData("G-Funk", "Hip-Hop")]
    public void Genres_land_in_the_family_a_listener_would_expect(string genre, string family) =>
        Assert.Equal(family, TagFamilies.FamilyOf(genre, TagKind.Genre));

    [Fact]
    public void A_genre_with_no_telling_word_stays_on_its_own() =>
        Assert.Null(TagFamilies.FamilyOf("Instrumental", TagKind.Genre));

    [Fact]
    public void A_moved_genre_goes_where_the_listener_put_it_everywhere_and_can_be_put_back()
    {
        var key = TagFamilies.OverrideKey("Pop Rock", TagKind.Genre);
        TagFamilies.UseOverrides(new Dictionary<string, string> { [key] = "Pop" });
        Assert.Equal("Pop", TagFamilies.FamilyOf("Pop Rock", TagKind.Style));
        Assert.Equal("Rock", TagFamilies.AutomaticFamilyOf("Pop Rock", TagKind.Genre));
        Assert.True(TagFamilies.IsMoved("pop rock ", TagKind.Genre));

        var grouped = TagFamilies.Group(
        [
            new TagItem { RatingKey = "1", Title = "Pop Rock", TagKind = TagKind.Genre, FilterValue = "1" },
            new TagItem { RatingKey = "2", Title = "Dance Pop", TagKind = TagKind.Genre, FilterValue = "2" },
        ], TagKind.Genre);
        var pop = Assert.Single(grouped);
        Assert.Equal("Pop", pop.Title);
        Assert.Equal(2, pop.Members.Count);

        TagFamilies.UseOverrides(new Dictionary<string, string> { [key] = "" });
        Assert.Null(TagFamilies.FamilyOf("Pop Rock", TagKind.Genre));

        TagFamilies.UseOverrides(null);
        Assert.Equal("Rock", TagFamilies.FamilyOf("Pop Rock", TagKind.Genre));
    }

    [Fact]
    public void Every_family_can_be_chosen_when_moving()
    {
        var names = TagFamilies.Names(TagKind.Genre);
        Assert.Contains("African", names);
        Assert.Contains("South Asian", names);
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.NotEmpty(TagFamilies.Names(TagKind.Mood));
        Assert.Empty(TagFamilies.Names(TagKind.Decade));
    }
}

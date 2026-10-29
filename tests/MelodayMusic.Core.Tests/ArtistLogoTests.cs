using MelodayMusic.Core.Models;
using MelodayMusic.Core.Logos;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Plex.Dto;
using MelodayMusic.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public class ArtistLogoTests
{
    [Fact]
    public void Hd_english_popular_logos_come_first()
    {
        const string json = """
        {
          "name": "Robyn",
          "musiclogo": [ { "id": "1", "url": "https://assets.fanart.tv/sd.png", "likes": "40", "lang": "en" } ],
          "hdmusiclogo": [
            { "id": "2", "url": "https://assets.fanart.tv/hd-few.png", "likes": "1", "lang": "en" },
            { "id": "3", "url": "https://assets.fanart.tv/hd-se.png", "likes": "9", "lang": "se" },
            { "id": "4", "url": "https://assets.fanart.tv/hd-many.png", "likes": "5", "lang": "" }
          ]
        }
        """;
        var logos = FanartTv.ParseLogos(json);
        Assert.Equal(["hd-many", "hd-few", "hd-se", "sd"], logos.Select(l => Path.GetFileNameWithoutExtension(l.Url)));
    }

    [Fact]
    public void No_logos_parses_to_empty()
    {
        Assert.Empty(FanartTv.ParseLogos("""{ "name": "Nobody", "artistthumb": [] }"""));
    }

    [Fact]
    public void MusicBrainz_id_is_read_from_plex_guids()
    {
        var guids = new List<TagDto> { new() { Id = "plex://artist/5d07bbfd403c6402904a65dc" }, new() { Id = "mbid://5a8e07d5-d932-4484-a7f7-e700793a9c94" } };
        Assert.Equal("5a8e07d5-d932-4484-a7f7-e700793a9c94", PlexMapper.MusicBrainzId(guids));
        Assert.Null(PlexMapper.MusicBrainzId([new() { Id = "mbid://not-a-guid" }]));
        Assert.Null(PlexMapper.MusicBrainzId(null));
    }

    [Theory]
    [InlineData("Maroon 5/Nicki Minaj", "Nicki Minaj", true)]
    [InlineData("Calvin Harris ft. Katy Perry & Big Sean", "Katy Perry", true)]
    [InlineData("Calvin Harris ft. Katy Perry & Big Sean", "Big Sean", true)]
    [InlineData("nicki minaj", "Nicki Minaj", true)]
    [InlineData("Ava Max", "Ava", false)]
    [InlineData("Java Jive", "Ava", false)]
    [InlineData("Ava And Friends", "Ava", true)]
    [InlineData("Beyoncé", "Beyoncé", true)]
    [InlineData(null, "Beyoncé", false)]
    public void Credits_name_artists_as_whole_names(string? credit, string artist, bool expected)
    {
        Assert.Equal(expected, ArtistCredit.Mentions(credit, artist));
    }

    [Theory]
    [InlineData("Nicki Minaj Radio", "Nicki Minaj", true)]
    [InlineData("Featuring Nicki Minaj", "Nicki Minaj", true)]
    [InlineData("Adele Essentials", "Adele", true)]
    [InlineData("This Is Robyn", "Robyn", true)]
    [InlineData("Robynesque", "Robyn", false)]
    [InlineData("2018 Mix", "Nicki Minaj", false)]
    public void Playlist_titles_name_artists_as_whole_words(string title, string artist, bool expected)
    {
        Assert.Equal(expected, ArtistCredit.NamedIn(title, artist));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Secret_round_trips_encrypted()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "secret-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var store = new ProtectedSecret(path, "Test", NullLogger<ProtectedSecret>.Instance);
            store.Save("abc123-key");
            Assert.True(store.HasSecret);
            Assert.DoesNotContain("abc123-key", File.ReadAllText(path));
            Assert.Equal("abc123-key", store.Load());
            store.Clear();
            Assert.False(store.HasSecret);
            Assert.Null(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class LogoFolderTests
{
    private static Artist A(string title) => new() { RatingKey = title, Title = title };

    [Theory]
    [InlineData("Beyonce", "Beyoncé")]
    [InlineData("_NSYNC", "*NSYNC")]
    [InlineData("Aly and AJ", "Aly & AJ")]
    [InlineData("Weeknd", "The Weeknd")]
    [InlineData("ANOTR", "anotr")]
    public void Names_match_without_case_accents_or_punctuation(string file, string artist) =>
        Assert.Equal(LogoFolder.Normalize(artist), LogoFolder.Normalize(file));

    [Fact]
    public void The_alone_is_still_a_name() => Assert.Equal("the", LogoFolder.Normalize("The"));

    [Fact]
    public void Logos_pair_with_artists_and_the_rest_are_left_over()
    {
        var logos = new[] { new LogoFile("Adele", "a.png"), new LogoFile("Nobody Here", "n.png"), new LogoFile("adele", "b.png") };
        var (matched, unmatched) = LogoFolder.Match(logos, [A("Adele"), A("AJR")]);
        var only = Assert.Single(matched);
        Assert.Equal("Adele", only.Artist.Title);
        Assert.Equal("a.png", only.Logo.Path);
        Assert.Equal(["Nobody Here", "adele"], unmatched.Select(u => u.Name));
    }

    [Fact]
    public void Folders_and_loose_images_are_both_found()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "logofolder-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Adele"));
            File.WriteAllText(Path.Combine(root, "Adele", "cover.jpg"), "");
            File.WriteAllText(Path.Combine(root, "Adele", "logo.png"), "");
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            File.WriteAllText(Path.Combine(root, "AJR.png"), "");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "");
            var found = LogoFolder.Find(root);
            Assert.Equal(["Adele", "AJR"], found.Select(f => f.Name));
            Assert.EndsWith("logo.png", found[0].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

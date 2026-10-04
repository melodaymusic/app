using HumeMusic.Core.Models;
using HumeMusic.Core.Pins;
using Microsoft.Extensions.Logging.Abstractions;

namespace HumeMusic.Core.Tests;

public sealed class PinStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hume-pins-" + Guid.NewGuid().ToString("N"));
    private string PathFor => System.IO.Path.Combine(_dir, "pins.json");

    private PinStore NewStore() => new(PathFor, NullLogger<PinStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Pins_supported_kinds_and_rejects_others()
    {
        var store = NewStore();
        Assert.True(store.Pin("s1", new Artist { RatingKey = "1", Title = "A" }));
        Assert.True(store.Pin("s1", new Album { RatingKey = "2", Title = "B" }));
        Assert.True(store.Pin("s1", new Playlist { RatingKey = "3", Title = "C", Composite = "/c" }));
        Assert.False(store.Pin("s1", new Station { RatingKey = "builder:albums", Title = "Album Mix Builder", StationKind = StationKind.AlbumMixBuilder }));
        var pins = store.GetPins("s1");
        Assert.Equal(3, pins.Count);
        Assert.Equal("/c", pins.Single(p => p.Kind == PinKind.Playlist).Thumb);
    }

    [Fact]
    public void Pins_can_be_arranged_in_any_order_and_a_new_pin_still_goes_first()
    {
        var store = NewStore();
        foreach (var k in new[] { "1", "2", "3", "4" }) store.Pin("s1", new Artist { RatingKey = k, Title = k });
        Assert.Equal(["4", "3", "2", "1"], store.GetPins("s1").Select(p => p.RatingKey));

        store.Reorder("s1", ["1", "3", "4", "2"]);
        Assert.Equal(["1", "3", "4", "2"], store.GetPins("s1").Select(p => p.RatingKey));

        // Pins left out of an arrangement follow the named ones.
        store.Reorder("s1", ["2"]);
        Assert.Equal(["2", "1", "3", "4"], store.GetPins("s1").Select(p => p.RatingKey));

        store.Pin("s1", new Artist { RatingKey = "5", Title = "5" });
        Assert.Equal(["5", "2", "1", "3", "4"], store.GetPins("s1").Select(p => p.RatingKey));

        // It survives a restart.
        Assert.Equal(["5", "2", "1", "3", "4"], NewStore().GetPins("s1").Select(p => p.RatingKey));
    }

    [Fact]
    public void Moving_a_pin_steps_it_earlier_or_later_and_stops_at_the_ends()
    {
        var store = NewStore();
        foreach (var k in new[] { "1", "2", "3" }) store.Pin("s1", new Artist { RatingKey = k, Title = k });
        store.Pin("s2", new Artist { RatingKey = "9", Title = "other" });
        Assert.Equal(["3", "2", "1"], store.GetPins("s1").Select(p => p.RatingKey));
        Assert.True(store.Move("s1", "1", -1));
        Assert.Equal(["3", "1", "2"], store.GetPins("s1").Select(p => p.RatingKey));
        Assert.True(store.Move("s1", "3", 5));
        Assert.Equal(["1", "2", "3"], store.GetPins("s1").Select(p => p.RatingKey));
        Assert.False(store.Move("s1", "3", 1));
        Assert.False(store.Move("s1", "1", -1));
        Assert.Equal((1, 3), store.Position("s1", "2"));
        Assert.Equal((-1, 3), store.Position("s1", "missing"));
        Assert.Single(store.GetPins("s2"));
    }

    [Fact]
    public void Pins_saved_before_they_could_be_arranged_keep_their_order()
    {
        Directory.CreateDirectory(_dir);
        var t0 = DateTimeOffset.UtcNow;
        File.WriteAllText(PathFor, System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { ServerId = "s1", RatingKey = "1", Kind = "Artist", Title = "Old", PinnedAt = t0 },
            new { ServerId = "s1", RatingKey = "2", Kind = "Artist", Title = "Mid", PinnedAt = t0.AddMinutes(1) },
            new { ServerId = "s1", RatingKey = "3", Kind = "Artist", Title = "New", PinnedAt = t0.AddMinutes(2) },
        }));
        Assert.Equal(["3", "2", "1"], NewStore().GetPins("s1").Select(p => p.RatingKey));
    }

    [Fact]
    public void Anything_can_be_pinned_and_made_items_come_back_as_they_were()
    {
        var store = NewStore();
        var station = new Station { RatingKey = "station:local:genre/9", Title = "Pop Radio", StationKind = StationKind.Genre, Tag = "9", Colour = "Cobalt", IsLocal = true };
        var artistMix = new ArtistMix { RatingKey = "mix:5", Title = "Tove Lo Mix", Artist = new Artist { RatingKey = "5", Title = "Tove Lo", Thumb = "/t" } };
        var tagMix = new TagMix { RatingKey = "mix:genre:Pop", Title = "Pop Mix", TagKind = MixTagKind.Genre, Tag = "Pop" };
        var builder = new Station { RatingKey = "builder:artists", Title = "Artist Mix Builder", StationKind = StationKind.ArtistMixBuilder };
        Assert.True(store.Pin("s1", station, thumb: @"C:\covers.png"));
        Assert.True(store.Pin("s1", artistMix));
        Assert.True(store.Pin("s1", tagMix));
        Assert.True(store.Pin("s1", new Track { RatingKey = "77", Title = "Song" }));
        Assert.False(store.Pin("s1", builder));

        var pins = store.GetPins("s1");
        Assert.Equal(4, pins.Count);
        var back = pins.Select(p => ItemRefs.ToItem(p.Kind, p.RatingKey, p.Title, p.Thumb, p.Data)).ToList();
        Assert.All(back, Assert.NotNull);
        var s = Assert.IsType<Station>(back.Single(b => b is Station));
        Assert.Equal((StationKind.Genre, "9", "Cobalt", true), (s.StationKind, s.Tag, s.Colour, s.IsLocal));
        Assert.Equal(@"C:\covers.png", pins.Single(p => p.Kind == PinKind.Station).Thumb);
        Assert.Equal("Tove Lo", Assert.IsType<ArtistMix>(back.Single(b => b is ArtistMix)).Artist.Title);
        Assert.Equal("Pop", Assert.IsType<TagMix>(back.Single(b => b is TagMix)).Tag);
        Assert.IsType<Track>(back.Single(b => b is Track));
        // A still cover can be added to a pin after it is made.
        store.SetThumb("s1", "mix:5", @"C:\covers.png");
        Assert.Equal(@"C:\covers.png", NewStore().GetPins("s1").Single(p => p.RatingKey == "mix:5").Thumb);
        Assert.True(ItemRefs.IsSynthetic(PinKind.Station));
        Assert.False(ItemRefs.IsSynthetic(PinKind.Album));
    }

    [Fact]
    public void Duplicate_pins_are_prevented()
    {
        var store = NewStore();
        Assert.True(store.Pin("s1", new Album { RatingKey = "2", Title = "B" }));
        Assert.False(store.Pin("s1", new Album { RatingKey = "2", Title = "B again" }));
        Assert.Single(store.GetPins("s1"));
    }

    [Fact]
    public void Same_title_different_items_are_distinct()
    {
        var store = NewStore();
        store.Pin("s1", new Album { RatingKey = "10", Title = "Greatest Hits" });
        store.Pin("s1", new Album { RatingKey = "11", Title = "Greatest Hits" });
        Assert.Equal(2, store.GetPins("s1").Count);
    }

    [Fact]
    public void Pins_are_scoped_per_server_and_ordered_newest_first()
    {
        var store = NewStore();
        var t0 = DateTimeOffset.UtcNow;
        store.Pin("s1", new Artist { RatingKey = "1", Title = "Old" }, t0);
        store.Pin("s1", new Artist { RatingKey = "2", Title = "New" }, t0.AddMinutes(1));
        store.Pin("s2", new Artist { RatingKey = "1", Title = "Other server" }, t0);
        Assert.Equal(["2", "1"], store.GetPins("s1").Select(p => p.RatingKey));
        Assert.Single(store.GetPins("s2"));
        Assert.True(store.IsPinned("s2", "1"));
        Assert.False(store.IsPinned("s2", "2"));
    }

    [Fact]
    public void Unpin_removes_and_raises_changed()
    {
        var store = NewStore();
        var changes = 0;
        store.Changed += (_, _) => changes++;
        store.Pin("s1", new Artist { RatingKey = "1", Title = "A" });
        Assert.True(store.Unpin("s1", "1"));
        Assert.False(store.Unpin("s1", "1"));
        Assert.Empty(store.GetPins("s1"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Pins_persist_across_instances()
    {
        NewStore().Pin("s1", new Playlist { RatingKey = "7", Title = "Road trip" });
        var reopened = NewStore();
        var pin = Assert.Single(reopened.GetPins("s1"));
        Assert.Equal("7", pin.RatingKey);
        Assert.Equal(PinKind.Playlist, pin.Kind);
        Assert.Equal("Road trip", pin.Title);
    }

    [Fact]
    public void Missing_items_are_removed()
    {
        var store = NewStore();
        store.Pin("s1", new Album { RatingKey = "1", Title = "Gone" });
        store.Pin("s1", new Album { RatingKey = "2", Title = "Here" });
        Assert.Equal(1, store.RemoveMissing("s1", ["1"]));
        Assert.Equal("2", Assert.Single(store.GetPins("s1")).RatingKey);
    }

    [Fact]
    public void Corrupt_file_is_tolerated()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, "{ not json");
        var store = NewStore();
        Assert.Empty(store.GetPins("s1"));
        Assert.True(store.Pin("s1", new Artist { RatingKey = "1", Title = "A" }));
        Assert.Single(NewStore().GetPins("s1"));
    }
}

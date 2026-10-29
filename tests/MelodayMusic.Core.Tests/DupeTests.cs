using MelodayMusic.Core.Meloday;

namespace MelodayMusic.Core.Tests;

public class DupeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static DupeSide S(string key) => new(key, "Song " + key, "Artist", "Album", "/thumb/" + key);

    private static DupePair P(string keeper, string dupe, int minutes = 0, string server = "srv") =>
        new(server, S(keeper), S(dupe), Now.AddMinutes(minutes));

    [Fact]
    public void A_pair_maps_the_dupe_to_its_keeper_and_keepers_stay_themselves()
    {
        var store = new DupeStore();
        Assert.True(store.Add(P("chick", "bitch")));
        Assert.Equal("chick", store.KeeperOf("srv", "bitch"));
        Assert.Equal("chick", store.KeeperOf("srv", "chick"));
        Assert.Equal("other", store.KeeperOf("srv", "other"));
        Assert.Equal("bitch", store.KeeperOf("elsewhere", "bitch"));
        Assert.Equal(new Dictionary<string, string> { ["bitch"] = "chick" }, store.Map("srv"));
    }

    [Fact]
    public void A_song_cannot_be_its_own_dupe_nor_the_dupe_of_its_own_dupe()
    {
        var store = new DupeStore();
        Assert.False(store.Add(P("a", "a")));
        Assert.True(store.Add(P("a", "b")));
        Assert.False(store.Add(P("b", "a")));
        Assert.Single(store.For("srv"));
    }

    [Fact]
    public void Pairs_never_chain_a_keeper_that_is_a_dupe_goes_to_its_keeper()
    {
        var store = new DupeStore();
        store.Add(P("b", "c"));
        store.Add(P("c", "a"));
        Assert.Equal("b", store.KeeperOf("srv", "a"));
        Assert.Equal("b", store.KeeperOf("srv", "c"));
    }

    [Fact]
    public void When_a_keeper_becomes_a_dupe_its_dupes_follow_it()
    {
        var store = new DupeStore();
        store.Add(P("b", "a"));
        store.Add(P("c", "b"));
        Assert.Equal("c", store.KeeperOf("srv", "a"));
        Assert.Equal("c", store.KeeperOf("srv", "b"));
        Assert.Equal(2, store.For("srv").Count);
    }

    [Fact]
    public void Choosing_a_different_keeper_for_a_dupe_replaces_the_pair()
    {
        var store = new DupeStore();
        store.Add(P("a", "b"));
        store.Add(P("c", "b", minutes: 5));
        Assert.Equal("c", store.KeeperOf("srv", "b"));
        Assert.Single(store.For("srv"));
    }

    [Fact]
    public void A_pair_can_be_undone_and_the_newest_come_first()
    {
        var store = new DupeStore();
        store.Add(P("a", "b", 0));
        store.Add(P("c", "d", 10));
        Assert.Equal(["d", "b"], store.For("srv").Select(p => p.Dupe.Key));
        Assert.True(store.Remove("srv", "b"));
        Assert.False(store.Remove("srv", "b"));
        Assert.Equal("b", store.KeeperOf("srv", "b"));
        Assert.Equal(["d"], store.For("srv").Select(p => p.Dupe.Key));
    }

    [Fact]
    public void Changes_are_announced()
    {
        var store = new DupeStore();
        var raised = 0;
        store.Changed += (_, _) => raised++;
        store.Add(P("a", "b"));
        store.Remove("srv", "b");
        store.Remove("srv", "b");
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Pairs_are_kept_between_runs_per_server()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-dupes-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new DupeStore(path);
            first.Add(P("a", "b"));
            first.Add(P("x", "y", server: "other"));
            var again = new DupeStore(path);
            Assert.Equal("a", again.KeeperOf("srv", "b"));
            Assert.Equal("x", again.KeeperOf("other", "y"));
            Assert.Equal("Song a", again.For("srv").Single().Keeper.Title);
            File.WriteAllText(path, "not json");
            Assert.Empty(new DupeStore(path).For("srv"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_list_has_each_dupe_replaced_by_its_keeper_and_a_song_met_twice_kept_once()
    {
        var map = new Dictionary<string, string> { ["bitch"] = "chick", ["old"] = "new" };
        Assert.Equal(["x", "chick", "y", "new"], Dupes.Collapse(["x", "bitch", "y", "old"], map));
        Assert.Equal(["chick", "y"], Dupes.Collapse(["chick", "y", "bitch"], map));
        Assert.Equal(["y", "chick"], Dupes.Collapse(["y", "bitch", "chick"], map));
        Assert.Equal(["a", "b"], Dupes.Collapse(["a", "b"], new Dictionary<string, string>()));
    }
}

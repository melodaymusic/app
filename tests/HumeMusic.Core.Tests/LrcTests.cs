using HumeMusic.Core.Lyrics;

namespace HumeMusic.Core.Tests;

public class LrcTests
{
    [Fact]
    public void Synced_lines_are_read_in_time_order_each_ending_where_the_next_begins()
    {
        var lines = LrcLibClient.ParseLrc("[ar:Someone]\n[00:12.50] First line\n[00:15.00][01:02.00] Chorus\n[00:20.10] Second\n[00:25.00] ");
        // The blank at 0:25 is a break before the chorus comes back, kept as one.
        Assert.Equal(["First line", "Chorus", "Second", "", "Chorus"], lines.Select(l => l.Text));
        Assert.Equal(12.5, lines[0].Start!.Value.TotalSeconds, 2);
        Assert.Equal(15, lines[0].End!.Value.TotalSeconds, 2);
        Assert.Equal(62, lines[4].Start!.Value.TotalSeconds, 2);
        Assert.Null(lines[4].End);
    }

    [Fact]
    public void Tags_and_text_without_times_are_set_aside() =>
        Assert.Empty(LrcLibClient.ParseLrc("[ti:Title]\nno time here\n"));
}

public class LeadArtistTests
{
    [Theory]
    [InlineData("Future/Nicki Minaj", "Future")]
    [InlineData("Charli xcx, Ariana Grande", "Charli xcx")]
    [InlineData("Calvin Harris feat. Rihanna", "Calvin Harris")]
    [InlineData("Simon & Garfunkel", "Simon")]
    [InlineData("Madonna", "Madonna")]
    public void The_lead_artist_is_the_first_credited(string credit, string lead) =>
        Assert.Equal(lead, LrcLibClient.LeadArtist(credit));
}

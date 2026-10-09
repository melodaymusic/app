using HumeMusic.Core.Plex;
using HumeMusic.Core.Plex.Dto;

namespace HumeMusic.Core.Tests;

public class PlexItemArtTests
{
    [Fact]
    public void A_playlists_art_and_logo_are_the_ones_plex_lists()
    {
        var playlist = PlexMapper.ToPlaylist(new MetadataDto
        {
            RatingKey = "9", Title = "Mix", Art = "/library/metadata/9/art/1",
            Image = [new() { Type = "coverPoster", Url = "/library/metadata/9/thumb/1" }, new() { Type = "clearLogo", Url = "/library/metadata/9/clearLogo/1" }],
        });
        Assert.Equal("/library/metadata/9/art/1", playlist.Art);
        Assert.Equal("/library/metadata/9/clearLogo/1", playlist.Logo);
    }

    [Fact]
    public void An_item_with_no_logo_listed_has_none_and_every_kind_of_item_reads_it()
    {
        Assert.Null(PlexMapper.ToArtist(new MetadataDto { RatingKey = "1", Title = "A", Image = [new() { Type = "coverPoster", Url = "/x" }] }).Logo);
        Assert.Null(PlexMapper.ToAlbum(new MetadataDto { RatingKey = "2", Title = "B" }).Logo);
        var logo = new List<ImageDto> { new() { Type = "clearLogo", Url = "/logo" } };
        Assert.Equal("/logo", PlexMapper.ToArtist(new MetadataDto { RatingKey = "1", Title = "A", Image = logo }).Logo);
        Assert.Equal("/logo", PlexMapper.ToAlbum(new MetadataDto { RatingKey = "2", Title = "B", Image = logo }).Logo);
        Assert.Equal("/logo", PlexMapper.ToCollection(new MetadataDto { RatingKey = "3", Title = "C", Image = logo }).Logo);
    }

    [Fact]
    public void Changing_an_items_details_names_its_type_and_key_and_locks_what_it_sets()
    {
        var query = PlexServerClient.DetailsQuery(PlexTypes.Album, "42", "New name", null).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("9", query["type"]);
        Assert.Equal("42", query["id"]);
        Assert.Equal("New name", query["title.value"]);
        Assert.Equal("1", query["title.locked"]);
        Assert.Equal(string.Empty, query["summary.value"]);
        Assert.Equal("1", query["summary.locked"]);
        Assert.Equal(18, PlexTypes.Collection);
    }
}

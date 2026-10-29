using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class TagCovers
{
    public const int Count = 3;

    private readonly PlexSession _session;
    private readonly ILogger<TagCovers> _log;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<Album>>> _covers = new();
    private readonly SemaphoreSlim _gate = new(4, 4);

    public TagCovers(PlexSession session, ILogger<TagCovers> log)
    {
        _session = session;
        _log = log;
        _session.StateChanged += (_, _) => _covers.Clear();
    }

    public Task<IReadOnlyList<Album>> GetAsync(TagItem tag) =>
        _covers.GetOrAdd($"{_session.Section?.Key}|{tag.TagKind}|{tag.FilterValue}", _ => LoadAsync(tag));

    private async Task<IReadOnlyList<Album>> LoadAsync(TagItem tag)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (tag.IsFamily)
            {
                var picked = new List<Album>();
                foreach (var member in tag.Members.Take(Count * 2))
                {
                    if (picked.Count == Count) break;
                    var top = await TopAsync(client, section.Key, member, Count).ConfigureAwait(false);
                    if (top.FirstOrDefault(a => picked.All(p => p.RatingKey != a.RatingKey)) is { } album) picked.Add(album);
                }
                return picked;
            }
            return await TopAsync(client, section.Key, tag, Count).ConfigureAwait(false);
        }
        catch (PlexException ex)
        {
            _log.LogDebug("Covers for {Tag} unavailable: {Message}", tag.Title, ex.Message);
            _covers.TryRemove($"{section.Key}|{tag.TagKind}|{tag.FilterValue}", out _);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<Album>> TopAsync(PlexServerClient client, string section, TagItem tag, int count)
    {
        var field = tag.TagKind == TagKind.Mood ? "track.mood" : PlexServerClient.TagField(tag.TagKind);
        var albums = (await client.GetAlbumsAsync(section, 0, count, "viewCount:desc", default,
            new KeyValuePair<string, string?>(field, tag.FilterValue)).ConfigureAwait(false)).Items;
        if (albums.Count == 0 && tag.TagKind == TagKind.Mood)
            albums = (await client.GetAlbumsAsync(section, 0, count, "viewCount:desc", default,
                new KeyValuePair<string, string?>("mood", tag.FilterValue)).ConfigureAwait(false)).Items;
        return albums.Where(a => !string.IsNullOrEmpty(a.Thumb)).Take(count).ToList();
    }
}

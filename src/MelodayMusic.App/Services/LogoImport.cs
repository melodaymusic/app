using MelodayMusic.Core.Logos;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record LogoImportResult(int Found, int Imported, IReadOnlyList<string> Unmatched);

public sealed class LogoImport
{
    private const int PageSize = 1000;

    private readonly PlexSession _session;
    private readonly ArtistLogoService _logos;
    private readonly ILogger<LogoImport> _log;

    public LogoImport(PlexSession session, ArtistLogoService logos, ILogger<LogoImport> log)
    {
        _session = session;
        _logos = logos;
        _log = log;
    }

    public async Task<LogoImportResult?> ImportAsync(string folder)
    {
        if (_session.Client is not { } client || _session.Section is not { } section || !Directory.Exists(folder)) return null;
        var files = LogoFolder.Find(folder);
        if (files.Count == 0) return new LogoImportResult(0, 0, []);

        var artists = new List<Artist>();
        try
        {
            for (var start = 0; ; start += PageSize)
            {
                var page = await client.GetArtistsAsync(section.Key, start, PageSize, "titleSort", default,
                    new KeyValuePair<string, string?>("includeGuids", "1")).ConfigureAwait(false);
                artists.AddRange(page.Items);
                if (page.Items.Count < PageSize) break;
            }
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Logo import couldn't list the library's artists: {Message}", ex.Message);
            return null;
        }

        var (matched, unmatched) = LogoFolder.Match(files, artists);
        var imported = await _logos.ImportAsync(matched).ConfigureAwait(false);
        if (unmatched.Count > 0) _log.LogInformation("{Count} logos had no artist of that name in the library: {Names}", unmatched.Count, string.Join(", ", unmatched.Select(u => u.Name)));
        return new LogoImportResult(files.Count, imported, unmatched.Select(u => u.Name).ToList());
    }
}

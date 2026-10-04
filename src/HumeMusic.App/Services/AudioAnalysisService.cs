using System.Collections.Concurrent;
using System.Text.Json;
using HumeMusic.Core;
using HumeMusic.Core.Audio;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Meloday Music's own listening, for AutoMix: the tempo, beats, bars, key and energy of the stretch
/// of a song a mix uses (its opening for coming in, its closing minute for going out). Plex has no
/// beat analysis, so the audio itself is decoded (only that stretch is fetched) and analysed here,
/// one song at a time in the background; what's found is kept on this computer.
/// </summary>
public sealed class AudioAnalysisService
{
    private static readonly TimeSpan HeadLength = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan TailLength = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new();

    private readonly PlexSession _session;
    private readonly DownloadService _downloads;
    private readonly ILogger<AudioAnalysisService> _log;
    private readonly string _folder = Path.Combine(AppPaths.Root, "cache", "analysis");
    private readonly ConcurrentDictionary<string, Task<AudioAnalysis?>> _results = new();
    private readonly SemaphoreSlim _one = new(1, 1);

    public AudioAnalysisService(PlexSession session, DownloadService downloads, ILogger<AudioAnalysisService> log)
    {
        _session = session;
        _downloads = downloads;
        _log = log;
    }

    /// <summary>How a song opens (its first 50 seconds).</summary>
    public Task<AudioAnalysis?> HeadAsync(Track track) => Get(track, head: true);

    /// <summary>How a song closes (its last minute).</summary>
    public Task<AudioAnalysis?> TailAsync(Track track) => Get(track, head: false);

    private Task<AudioAnalysis?> Get(Track track, bool head) =>
        _results.GetOrAdd($"{track.RatingKey}|{(head ? "head" : "tail")}", key => LoadAsync(track, head, key));

    private sealed class Kept
    {
        public int Version { get; set; }
        public AudioAnalysis? Analysis { get; set; }
    }

    // Raised when the analysis itself changes, so what was kept is worked out again.
    private const int AnalysisVersion = 1;

    private async Task<AudioAnalysis?> LoadAsync(Track track, bool head, string key)
    {
        var path = Path.Combine(_folder, key.Replace('|', '-') + ".json");
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Kept>(await File.ReadAllTextAsync(path), Json) is { Version: AnalysisVersion, Analysis: { } kept })
                return kept;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        await _one.WaitAsync();
        try
        {
            var analysis = await Task.Run(() => Analyse(track, head));
            if (analysis is null)
            {
                _results.TryRemove(key, out _);
                return null;
            }
            try
            {
                Directory.CreateDirectory(_folder);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Kept { Version = AnalysisVersion, Analysis = analysis }, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            _log.LogInformation("Listened to the {Part} of {Track}: {Bpm:0.0} bpm ({Confidence:0.00}), {Key}",
                head ? "start" : "end", track.Title, analysis.Bpm, analysis.TempoConfidence, AudioAnalysis.KeyName(analysis.Key));
            return analysis;
        }
        finally
        {
            _one.Release();
        }
    }

    private AudioAnalysis? Analyse(Track track, bool head)
    {
        try
        {
            var duration = track.Duration;
            var from = head ? TimeSpan.Zero : duration - TailLength;
            if (from < TimeSpan.Zero) from = TimeSpan.Zero;
            var length = head ? HeadLength : TailLength;
            string source;
            var offsetInSource = from;
            if (_downloads.LocalUri(track) is { } local) source = local.LocalPath;
            else if (_session.Client is { } client)
            {
                var full = track.Media?.PartKey is null ? client.GetTrackAsync(track.RatingKey).GetAwaiter().GetResult() ?? track : track;
                if (full.Media?.Codec is { } codec && PlayerService.IsTranscodeOnly(codec))
                {
                    // Windows can't decode it: Plex's transcoder sends that stretch as MP3.
                    source = client.BuildTranscodeUri(full, 320, "analysis-" + Guid.NewGuid().ToString("N"), (long)from.TotalMilliseconds).ToString();
                    offsetInSource = TimeSpan.Zero;
                }
                else if (client.BuildDirectPlayUri(full) is { } direct) source = direct.ToString();
                else return null;
            }
            else return null;

            var samples = AudioDecoder.ReadWindow(source, offsetInSource, length);
            return BeatAnalyzer.Analyse(samples, AudioDecoder.AnalysisRate, from.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogDebug("Couldn't listen to {Track}: {Message}", track.Title, ex.Message);
            return null;
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Caching;

public sealed class ArtworkCache
{
    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new();
    private readonly SemaphoreSlim _downloads = new(6, 6);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures = new();

    public ArtworkCache(string directory, HttpClient http, ILogger<ArtworkCache> log)
    {
        _directory = directory;
        _http = http;
        _log = log;
        Directory.CreateDirectory(directory);
    }

    public string DirectoryPath => _directory;

    public Task<string?> GetAsync(string cacheKey, Uri source, CancellationToken ct = default)
    {
        var file = Path.Combine(_directory, Hash(cacheKey) + ".img");
        if (File.Exists(file)) return Task.FromResult<string?>(file);
        if (_failures.TryGetValue(cacheKey, out var failedAt) && DateTimeOffset.UtcNow - failedAt < TimeSpan.FromMinutes(5))
            return Task.FromResult<string?>(null);

        return _inflight.GetOrAdd(cacheKey, _ => DownloadAsync(cacheKey, source, file));
    }

    private async Task<string?> DownloadAsync(string cacheKey, Uri source, string file)
    {
        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(file)) return file;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await _http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _failures[cacheKey] = DateTimeOffset.UtcNow;
                _log.LogDebug("Artwork {Key} unavailable: {Status}", cacheKey, (int)response.StatusCode);
                return null;
            }
            var temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await using (var fs = File.Create(temp))
            {
                await response.Content.CopyToAsync(fs, cts.Token).ConfigureAwait(false);
            }
            if (new FileInfo(temp).Length == 0)
            {
                File.Delete(temp);
                _failures[cacheKey] = DateTimeOffset.UtcNow;
                return null;
            }
            File.Move(temp, file, overwrite: true);
            return file;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            _failures[cacheKey] = DateTimeOffset.UtcNow;
            _log.LogDebug("Artwork {Key} failed: {Message}", cacheKey, ex.Message);
            return null;
        }
        finally
        {
            _downloads.Release();
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    public long SizeBytes()
    {
        try { return new DirectoryInfo(_directory).EnumerateFiles().Sum(f => f.Length); }
        catch (IOException) { return 0; }
    }

    public void Trim(long maxBytes)
    {
        try
        {
            var files = new DirectoryInfo(_directory).EnumerateFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= maxBytes) break;
                total -= f.Length;
                try { f.Delete(); } catch (IOException) { }
            }
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Artwork cache trim failed");
        }
    }

    public void Clear()
    {
        foreach (var f in Directory.EnumerateFiles(_directory))
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        _failures.Clear();
    }

    private static string Hash(string key) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}

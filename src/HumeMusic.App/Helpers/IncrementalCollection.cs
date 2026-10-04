using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace HumeMusic.Helpers;

/// <summary>
/// A collection that pages items from Plex on demand as the list scrolls
/// (ListView/GridView call LoadMoreItemsAsync through ISupportIncrementalLoading), so large
/// libraries are never loaded into memory all at once.
/// </summary>
public sealed class IncrementalCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
{
    private readonly Func<int, int, CancellationToken, Task<PagedResult<T>>> _fetch;
    private readonly int _pageSize;
    private CancellationTokenSource _cts = new();
    private bool _hasMore = true;
    private bool _loading;

    public IncrementalCollection(Func<int, int, CancellationToken, Task<PagedResult<T>>> fetch, int pageSize = 100)
    {
        _fetch = fetch;
        _pageSize = pageSize;
    }

    /// <summary>Total items reported by the server, when known.</summary>
    public int? TotalCount { get; private set; }

    public bool HasMoreItems => _hasMore && !_loading;

    public bool IsLoading => _loading;

    public event EventHandler<Exception>? LoadFailed;
    public event EventHandler? PageLoaded;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) =>
        AsyncInfo.Run(ct => LoadPageAsync(ct));

    private async Task<LoadMoreItemsResult> LoadPageAsync(CancellationToken ct)
    {
        if (_loading || !_hasMore) return new LoadMoreItemsResult { Count = 0 };
        _loading = true;
        var token = _cts.Token;
        try
        {
            var page = await _fetch(Count, _pageSize, token);
            if (token.IsCancellationRequested) return new LoadMoreItemsResult { Count = 0 };
            foreach (var item in page.Items) Add(item);
            TotalCount = page.TotalSize;
            _hasMore = page.Items.Count > 0 && (page.TotalSize is { } total ? Count < total : page.Items.Count >= _pageSize);
            PageLoaded?.Invoke(this, EventArgs.Empty);
            return new LoadMoreItemsResult { Count = (uint)page.Items.Count };
        }
        catch (OperationCanceledException)
        {
            return new LoadMoreItemsResult { Count = 0 };
        }
        catch (PlexException ex)
        {
            _hasMore = false;
            LoadFailed?.Invoke(this, ex);
            return new LoadMoreItemsResult { Count = 0 };
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Loads the first page explicitly (e.g. before the list is shown).</summary>
    public Task LoadFirstPageAsync() => LoadPageAsync(CancellationToken.None);

    public void Reset()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        Clear();
        TotalCount = null;
        _hasMore = true;
        _loading = false;
    }
}

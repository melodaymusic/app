using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace MelodayMusic.Helpers;

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

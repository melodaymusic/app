using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed record SortOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public abstract partial class PagedListViewModel<T> : ViewModelBase
{
    private int _loadedVersion = -1;

    protected PagedListViewModel(PlexSession session, ILogger log, IReadOnlyList<SortOption> sorts) : base(session, log)
    {
        SortOptions = sorts;
        SelectedSort = sorts[0];
        Items = CreateCollection();
    }

    public IReadOnlyList<SortOption> SortOptions { get; }

    [ObservableProperty] public partial SortOption SelectedSort { get; set; }
    [ObservableProperty] public partial IncrementalCollection<T> Items { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    protected abstract Task<PagedResult<T>> FetchAsync(int start, int size, string sort, CancellationToken ct);
    protected abstract string Noun(int count);

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_loadedVersion >= 0) _ = ReloadAsync();
    }

    public void ApplySort(string? value)
    {
        if (value is null) return;
        var match = SortOptions.FirstOrDefault(o => o.Value == value);
        if (match is not null && match != SelectedSort) SelectedSort = match;
    }

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion == libraryVersion && Items.Count > 0) return;
        _loadedVersion = libraryVersion;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var collection = CreateCollection();
        Items = collection;
        CountText = string.Empty;
        IsEmpty = false;
        await RunAsync(async () =>
        {
            await collection.LoadFirstPageAsync();
            if (ReferenceEquals(Items, collection)) IsEmpty = collection.Count == 0 && !HasError;
        }, isCurrent: () => ReferenceEquals(Items, collection));
    }

    private IncrementalCollection<T> CreateCollection()
    {
        var sort = SelectedSort?.Value ?? string.Empty;
        var collection = new IncrementalCollection<T>((start, size, ct) => FetchAsync(start, size, sort, ct), 120);
        collection.LoadFailed += (_, ex) =>
        {
            if (ReferenceEquals(Items, collection)) ErrorMessage = Describe(ex);
        };
        collection.PageLoaded += (_, _) =>
        {
            if (!ReferenceEquals(Items, collection)) return;
            var total = collection.TotalCount ?? collection.Count;
            CountText = Noun(total);
        };
        return collection;
    }
}

internal static class LocalSort
{
    public static PagedResult<T> Page<T>(IReadOnlyList<T> all, int start, int size) => new()
    {
        Items = all.Skip(start).Take(size).ToList(),
        Offset = start,
        TotalSize = all.Count,
    };
}

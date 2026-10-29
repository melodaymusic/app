using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Home;
using MelodayMusic.Core.Settings;
using Microsoft.UI.Xaml;

namespace MelodayMusic.Services;

public sealed partial class HomeSection : ObservableObject
{
    internal Action<HomeSection>? ShownChanged;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool IsAvailable { get; init; } = true;
    public bool IsCurated => HomeLayout.IsCurated(Id);
    public Visibility CuratedVisibility => IsCurated ? Visibility.Visible : Visibility.Collapsed;
    public string? AvailabilityNote => IsAvailable ? null : "Connect Last.fm in Settings > Connections.";

    public string MoveUpLabel => $"Move {Title} up";
    public string MoveDownLabel => $"Move {Title} down";
    public string ShowLabel => $"Show {Title} on Home";

    [ObservableProperty] public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value) => ShownChanged?.Invoke(this);
}

public sealed partial class HomeLayoutService : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly LastFmService _lastFm;
    private IReadOnlyList<(string Id, string Title)> _available = [];

    public HomeLayoutService(SettingsStore settings, LastFmService lastFm)
    {
        _settings = settings;
        _lastFm = lastFm;
        ShowJumpBackIn = settings.Current.ShowJumpBackIn;
    }

    public event EventHandler? Changed;

    public ObservableCollection<HomeSection> Sections { get; } = [];

    [ObservableProperty] public partial bool ShowJumpBackIn { get; set; }

    partial void OnShowJumpBackInChanged(bool value)
    {
        if (_settings.Current.ShowJumpBackIn == value) return;
        _settings.Current.ShowJumpBackIn = value;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> VisibleOrder(IReadOnlyList<string> available) =>
        HomeLayout.Visible(HomeLayout.CuratedFirst(available), _settings.Current.HomeOrder, _settings.Current.HomeHidden);

    public void UpdateCatalog(IReadOnlyList<(string Id, string Title)> available)
    {
        _available = available.Where(a => _lastFm.HasApiKey || a.Id is not ("trending" or "trending.artists"))
            .ToList();
        var order = HomeLayout.CuratedFirst(_available.Select(a => a.Id)).ToList();
        _available = _available.OrderBy(a => order.IndexOf(a.Id)).ToList();
        HideNewLibraryHubs();
        RebuildSections();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Sections.Count) return;
        to = Math.Clamp(to, 0, Sections.Count - 1);
        if (from != to) Sections.Move(from, to);
        SaveOrder();
    }

    public void SaveOrder()
    {
        _settings.Current.HomeOrder = Sections.Select(s => s.Id).ToList();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _settings.Current.HomeOrder = [];
        _settings.Current.HomeHidden = [];
        _settings.Current.HomeKnown = [];
        HideNewLibraryHubs();
        _settings.Save();
        ShowJumpBackIn = true;
        RebuildSections();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Library hubs start hidden so a new install opens on Meloday's rails.
    private void HideNewLibraryHubs()
    {
        var current = _settings.Current;
        if (current.HomeKnown is null)
            current.HomeKnown = current.HomeOrder.Count > 0 || current.HomeHidden.Count > 0 ? _available.Select(a => a.Id).ToList() : [];
        var known = current.HomeKnown.ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var (id, _) in _available)
        {
            if (!known.Add(id)) continue;
            current.HomeKnown.Add(id);
            if (HomeLayout.IsLibraryHub(id) && !current.HomeHidden.Contains(id)) current.HomeHidden.Add(id);
            changed = true;
        }
        if (changed) _settings.Save();
    }

    private void RebuildSections()
    {
        var titles = _available.ToDictionary(a => a.Id, a => a.Title);
        var hidden = new HashSet<string>(_settings.Current.HomeHidden);
        var order = HomeLayout.Arrange(_available.Select(a => a.Id).ToList(), _settings.Current.HomeOrder);
        foreach (var s in Sections) s.ShownChanged = null;
        Sections.Clear();
        foreach (var id in order)
        {
            var chart = id is "trending" or "trending.artists";
            var shown = !hidden.Contains(id) && (id == "trending" ? _lastFm.TrendingOn : id == "trending.artists" ? _lastFm.TrendingArtistsOn : true);
            var section = new HomeSection { Id = id, Title = titles[id], IsShown = shown, IsAvailable = !chart || _lastFm.HasApiKey };
            section.ShownChanged = OnSectionShownChanged;
            Sections.Add(section);
        }
    }

    private void OnSectionShownChanged(HomeSection section)
    {
        var hidden = _settings.Current.HomeHidden;
        hidden.Remove(section.Id);
        if (!section.IsShown) hidden.Add(section.Id);
        _settings.Save();
        if (section.Id == "trending") _lastFm.SetTrending(section.IsShown);
        if (section.Id == "trending.artists") _lastFm.SetTrendingArtists(section.IsShown);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

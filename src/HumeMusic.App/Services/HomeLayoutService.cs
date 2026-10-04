using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Home;
using HumeMusic.Core.Settings;

namespace HumeMusic.Services;

/// <summary>One Home section as listed in Settings: its name and whether it is shown.</summary>
public sealed partial class HomeSection : ObservableObject
{
    internal Action<HomeSection>? ShownChanged;

    public required string Id { get; init; }
    public required string Title { get; init; }

    // Accessible names for the section's controls in Settings.
    public string MoveUpLabel => $"Move {Title} up";
    public string MoveDownLabel => $"Move {Title} down";
    public string ShowLabel => $"Show {Title} on Home";

    [ObservableProperty] public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value) => ShownChanged?.Invoke(this);
}

/// <summary>
/// The listener's arrangement of Home: section order and hidden sections (saved in settings), and
/// whether "Jump back in" is shown. Home reports the sections it has; Settings edits the arrangement;
/// Home re-lays itself out whenever it changes, without reloading anything from Plex.
/// </summary>
public sealed partial class HomeLayoutService : ObservableObject
{
    private readonly SettingsStore _settings;
    private IReadOnlyList<(string Id, string Title)> _available = [];

    public HomeLayoutService(SettingsStore settings)
    {
        _settings = settings;
        ShowJumpBackIn = settings.Current.ShowJumpBackIn;
    }

    /// <summary>Raised when the order, visibility or "Jump back in" setting changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Every section Home has, in the listener's order (hidden ones included).</summary>
    public ObservableCollection<HomeSection> Sections { get; } = [];

    [ObservableProperty] public partial bool ShowJumpBackIn { get; set; }

    partial void OnShowJumpBackInChanged(bool value)
    {
        if (_settings.Current.ShowJumpBackIn == value) return;
        _settings.Current.ShowJumpBackIn = value;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The ids to show on Home, in order, from the sections available right now.</summary>
    public IReadOnlyList<string> VisibleOrder(IReadOnlyList<string> available) =>
        HomeLayout.Visible(available, _settings.Current.HomeOrder, _settings.Current.HomeHidden);

    /// <summary>Home reports the sections it has (in their default order) so Settings can list them.</summary>
    public void UpdateCatalog(IReadOnlyList<(string Id, string Title)> available)
    {
        _available = available;
        RebuildSections();
    }

    /// <summary>Moves a section up or down the list (also used after a drag reorder with from == to).</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= Sections.Count) return;
        to = Math.Clamp(to, 0, Sections.Count - 1);
        if (from != to) Sections.Move(from, to);
        SaveOrder();
    }

    /// <summary>Saves the current list order (after a drag reorder in Settings).</summary>
    public void SaveOrder()
    {
        _settings.Current.HomeOrder = Sections.Select(s => s.Id).ToList();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the default order with every section shown.</summary>
    public void Reset()
    {
        _settings.Current.HomeOrder = [];
        _settings.Current.HomeHidden = [];
        _settings.Save();
        ShowJumpBackIn = true;
        RebuildSections();
        Changed?.Invoke(this, EventArgs.Empty);
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
            var section = new HomeSection { Id = id, Title = titles[id], IsShown = !hidden.Contains(id) };
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
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>
/// Made for You explains Meloday's personalised recommendations by intent instead of presenting one
/// undifferentiated algorithmic row. Every section is backed by a signal the niche engine actually used.
/// </summary>
public sealed partial class MadeForYouViewModel : ViewModelBase
{
    private readonly MixService _mixes;
    private int _loadedVersion = -1;

    public MadeForYouViewModel(PlexSession session, MixService mixes, ILogger<MadeForYouViewModel> log) : base(session, log)
    {
        _mixes = mixes;
    }

    public ObservableCollection<MediaRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Description { get; private set; } =
        "Personal mixes shaped by what you play, what you return to, and when you listen.";

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion == libraryVersion && Rows.Count > 0) return;
        _loadedVersion = libraryVersion;
        await RunAsync(async () =>
        {
            var mixes = (await _mixes.GetNicheMixesAsync().ConfigureAwait(false))
                .OrderByDescending(m => m.Support).ToList();
            var rows = new List<MediaRow>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var now = DateTime.Now;
            var period = MelodayPeriods.At(now).Name;
            var weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            void Add(string title, string? subtitle, Func<NicheMix, bool> match, int take = 6)
            {
                var items = mixes.Where(m => !used.Contains(m.RatingKey) && match(m)).Take(take).ToList();
                if (items.Count == 0) return;
                foreach (var item in items) used.Add(item.RatingKey);
                rows.Add(new MediaRow
                {
                    Title = title,
                    Subtitle = subtitle,
                    Items = items.Cast<object>().ToList(),
                    Identifier = "made." + title.ToLowerInvariant().Replace(' ', '.'),
                    CardStyle = CardStyle.ColourBoard,
                });
            }

            Add("Your sound lately", "The corners of your library you keep coming back to", m => m.Focus == NicheFocus.Lately);
            Add(period is "Night" or "Late Night" ? "After dark" : "For right now",
                $"Patterns Meloday has learned for {period.ToLowerInvariant()} listening",
                m => string.Equals(m.Context, period, StringComparison.OrdinalIgnoreCase));
            if (weekend)
                Add("For the weekend", "Music that repeatedly shows up in your weekend listening", m => m.Context == "Weekend");
            else
                Add("For the week", "Music that repeatedly shows up in your weekday listening", m => m.Context == "Weekday");
            Add("Rediscover", "Older favourites and deeper cuts from music you already care about",
                m => m.Focus is NicheFocus.Rediscovery or NicheFocus.DeepCuts);
            Add("Your energy", "Tempo-based mixes from songs AutoMix has already analysed",
                m => m.Energy != NicheEnergy.Any);
            Add("Your circles", "Artists that repeatedly turn up together in your listening sessions",
                m => m.ArtistNames.Count >= 2);
            Add("Sonic neighbourhoods", "Nearby music found through Plex sonic analysis",
                m => m.Focus == NicheFocus.Sonic);
            Add("Your moods", "Specific mood and genre intersections in your taste",
                m => m.Mood is not null && m.Focus == NicheFocus.Blend && m.Context is null && m.Energy == NicheEnergy.Any);
            Add("More for you", null, _ => true, 10);

            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            IsEmpty = Rows.Count == 0;
            Description = Rows.Count == 0
                ? "Listen a little more and Meloday will start finding the specific corners of your taste."
                : "Personal mixes shaped by what you play, what you return to, and when you listen.";
        });
    }
}

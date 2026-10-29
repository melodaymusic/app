using System.Reflection;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using MelodayMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic;

public partial class App
{
    partial void RunUiSmoke() => _ = Environment.GetEnvironmentVariable("MELODAY_POLISH_OUTPUT") is not null ? CheckPolishUiAsync() : CheckRadioUiAsync();

    private async Task CheckRadioUiAsync()
    {
        var logger = Services.GetRequiredService<ILogger<App>>();
        try
        {
            var session = Services.GetRequiredService<PlexSession>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (!session.IsReady || Dialogs.Root is null) await Task.Delay(200, deadline.Token);
            await Task.Delay(1500, deadline.Token);
            Services.GetRequiredService<NavigationService>().Navigate(typeof(RadioPage));
            await Task.Delay(1500, deadline.Token);
            await CheckDialog(() => MixBuilderDialog.ShowAsync(StationKind.ArtistMixBuilder), true);
            await CheckDialog(() => MixBuilderDialog.ShowAsync(StationKind.AlbumMixBuilder), true);
            var tracks = (await session.Client!.GetTracksAsync(session.Section!.Key, 0, 2, ct: deadline.Token)).Items;
            if (tracks.Count < 2) throw new InvalidOperationException("Two tracks are required for the endpoint check");
            var local = Services.GetRequiredService<LocalRadio>();
            var artists = (await session.Client.GetArtistsAsync(session.Section.Key, 0, 6, "viewCount:desc", deadline.Token)).Items;
            var albums = (await session.Client.GetAlbumsAsync(session.Section.Key, 0, 2, "viewCount:desc", deadline.Token)).Items;
            var affinity = typeof(LocalRadio).GetMethod("ComputeAffinityAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await Task.WhenAll(artists.Select(a => (Task)affinity.Invoke(local, [a, deadline.Token])!));
            var artistRadio = await local.ForBuilt(new BuiltMix { RatingKey = "ui-smoke:artists", Title = "Test", ArtistKeys = artists.Take(2).Select(a => a.RatingKey).ToList() }).NextAsync(5, deadline.Token);
            var albumRadio = await local.ForBuilt(new BuiltMix { RatingKey = "ui-smoke:albums", Title = "Test", AlbumKeys = albums.Select(a => a.RatingKey).ToList() }).NextAsync(5, deadline.Token);
            if (artistRadio.Count == 0 || albumRadio.Count == 0) throw new InvalidOperationException("Builder radio generation returned no tracks");
            logger.LogInformation("UI SMOKE: parallel artist affinities completed; artist/album builders generated {ArtistTracks}/{AlbumTracks} tracks without playback", artistRadio.Count, albumRadio.Count);
            var type = typeof(SonicAdventureDialog).GetNestedType("End", BindingFlags.NonPublic)!;
            var endpoint = Activator.CreateInstance(type, "From", "Search", session.Client!, session.Section!.Key, (Action<Track>)(_ => { }))!;
            var show = type.GetMethod("Show")!;
            for (var i = 0; i < 4; i++)
            {
                show.Invoke(endpoint, [tracks[0]]);
                show.Invoke(endpoint, [tracks[1]]);
                show.Invoke(endpoint, [null]);
            }
            await CheckDialog(() => SonicAdventureDialog.ShowAsync(tracks[0]), false);
            var intelligence = Services.GetRequiredService<MusicIntelligence>();
            if (intelligence.IsAvailable)
            {
                var analysed = Services.GetRequiredService<MelodayEngine>().Snapshot().Values.Take(2).Select(p => p.Track).ToList();
                var endpoints = intelligence.IsMeloday && analysed.Count == 2 ? analysed : tracks;
                var path = await intelligence.AdventureAsync(endpoints[0], endpoints[1]).WaitAsync(TimeSpan.FromSeconds(30));
                logger.LogInformation("UI SMOKE: {Engine} adventure request completed with {Steps} steps", intelligence.Name, path.Count);
            }
            var live = new LiveRadio(session, Services.GetRequiredService<MixService>(), Services.GetRequiredService<LocalRadio>(),
                new Core.Radio.LiveScheduleStore(), Services.GetRequiredService<Core.Radio.RadioPlaybackStore>(),
                Services.GetRequiredService<Core.Radio.StationFeedbackStore>(), Services.GetRequiredService<Core.Settings.SettingsStore>(),
                intelligence, Services.GetRequiredService<ILogger<LiveRadio>>());
            var tuning = await live.TuneInAsync(Core.Radio.StationBrand.For(StationKind.Flagship)!.ToStation()).WaitAsync(TimeSpan.FromSeconds(45));
            if (tuning is null || tuning.Queue.Any(t => string.IsNullOrEmpty(tuning.ProgrammeOf(t.RatingKey))))
                throw new InvalidOperationException("Pulse did not attach programme labels to the generated queue");
            logger.LogInformation("UI SMOKE: Pulse generated {Tracks} labelled tracks; in-memory schedule, no playback", tuning.Queue.Count);
            logger.LogInformation("UI SMOKE PASS: artist/album builders opened, selections and removals worked, Sonic Adventure opened and endpoints updated repeatedly; playback preserved");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UI SMOKE FAIL");
            if (Environment.GetEnvironmentVariable("MELODAY_POLISH_OUTPUT") is not null) throw;
        }

        async Task CheckDialog(Func<Task> open, bool builder)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ContentDialog? dialog = null;
            async Task Observe()
            {
                while (dialog is null)
                {
                    await Task.Delay(100, timeout.Token);
                    dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Dialogs.Root!)
                        .SelectMany(p => Walk(p.Child)).OfType<ContentDialog>().FirstOrDefault();
                }
                try
                {
                    await Task.Delay(500, timeout.Token);
                    if (builder)
                    {
                        Button? add = null;
                        while (add is null)
                        {
                            add = Walk(dialog).OfType<Button>().FirstOrDefault(b =>
                                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).StartsWith("Add "));
                            if (add is null) await Task.Delay(100, timeout.Token);
                        }
                        new ButtonAutomationPeer(add).Invoke();
                        if (!dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Choosing a listening pick did not enable Play Radio");
                        var remove = Walk(dialog).OfType<Button>().First(b =>
                            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).StartsWith("Remove "));
                        new ButtonAutomationPeer(remove).Invoke();
                        if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Removing the last pick left Play Radio enabled");
                    }
                    else
                    {
                        var change = Walk(dialog).OfType<Button>().First(b => b.Content as string == "Change" && b.Visibility == Visibility.Visible);
                        new ButtonAutomationPeer(change).Invoke();
                    }
                }
                finally { dialog.Hide(); }
            }
            var observe = Observe();
            var opened = open();
            await Task.WhenAll(opened, observe).WaitAsync(TimeSpan.FromSeconds(25));
        }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject? parent)
    {
        if (parent is null) yield break;
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}

using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Services;
using MelodayMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace MelodayMusic;

public partial class App
{
    private async Task CheckPolishUiAsync()
    {
        var log = Services.GetRequiredService<ILogger<App>>();
        var output = Environment.GetEnvironmentVariable("MELODAY_POLISH_OUTPUT")!;
        Directory.CreateDirectory(output);
        var notes = new List<string>();
        var settings = Services.GetRequiredService<SettingsStore>();
        var originalTab = settings.Current.SettingsTab;
        var window = MainWindow!;
        var position = window.AppWindow.Position;
        var size = window.AppWindow.Size;
        var presenter = window.AppWindow.Presenter as OverlappedPresenter;
        var maximized = presenter?.State == OverlappedPresenterState.Maximized;
        var nav = Services.GetRequiredService<NavigationService>();
        var frame = (Frame)typeof(NavigationService).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nav)!;
        var originalPage = nav.CurrentPage ?? typeof(HomePage);
        var originalParameter = typeof(NavigationService).GetField("_parameter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nav);
        try
        {
            var session = Services.GetRequiredService<PlexSession>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            while (!session.IsReady || Dialogs.Root is null) await Task.Delay(200, deadline.Token);
            await Task.Delay(1500);
            Helpers.Motion.Set(false);
            frame = (Frame)typeof(NavigationService).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nav)!;
            originalPage = nav.CurrentPage ?? typeof(HomePage);
            originalParameter = typeof(NavigationService).GetField("_parameter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nav);
            if (Environment.GetEnvironmentVariable("MELODAY_INTEGRATIONS_ONLY") == "1")
            {
                await IntegrationChecks();
                notes.Add("OPTIONAL INTEGRATIONS PASS: no-key gating, independent services, Settings and artist catalogue checked.");
                return;
            }
            if (Environment.GetEnvironmentVariable("MELODAY_RADIO_COVERS_ONLY") == "1")
            {
                await RadioCovers();
                notes.Add("FIXED COVER BAR PASS: identical bar placement across single-line, wrapped and fitted labels.");
                return;
            }
            if (Environment.GetEnvironmentVariable("MELODAY_COVERS_ONLY") == "1")
            {
                await CoversAndSpotlight();
                notes.Add("COVER/SPOTLIGHT PASS: saved artwork, exact symbol colours, overlay exclusions and editorial layout checked.");
                return;
            }
            if (Environment.GetEnvironmentVariable("MELODAY_PLAYER_ONLY") == "1")
            {
                await PlayerLayout();
                notes.Add("PLAYER LAYOUT PASS: live labels keep title, artist and seek bar stable; selected dots have clear icon spacing.");
                return;
            }
            if (Environment.GetEnvironmentVariable("MELODAY_STAGE_ONLY") == "1")
            {
                await Stage();
                notes.Add("POLISH STAGE PASS: actual full-screen stage opened and closed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("MELODAY_SETTINGS_ONLY") == "1")
            {
                await SettingsSections();
                notes.Add("POLISH SETTINGS PASS: all eight sections at three window widths.");
                return;
            }
            var tracks = (await session.Client!.GetTracksAsync(session.Section!.Key, 0, 2)).Items;
            var artist = (await session.Client.GetArtistsAsync(session.Section.Key, 0, 1)).Items.FirstOrDefault();
            var album = (await session.Client.GetAlbumsAsync(session.Section.Key, 0, 1)).Items.FirstOrDefault();
            var playlist = (await session.Client.GetPlaylistsAsync(0, 1)).Items.FirstOrDefault();
            var collection = (await session.Client.GetCollectionsAsync(session.Section.Key)).FirstOrDefault();
            var tag = (await session.Client.GetTagsAsync(session.Section.Key, TagKind.Genre)).FirstOrDefault();
            var directory = (await Services.GetRequiredService<StationService>().GetCatalogAsync()).Directories.FirstOrDefault();
            var searchButton = Walk(window.Content as DependencyObject).OfType<Button>().First(b => b.Name == "SearchButton");
            new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(searchButton).Invoke();
            await Task.Delay(500);
            await Capture("global-search", window.Content as FrameworkElement);
            var searchBox = Walk(window.Content as DependencyObject).OfType<AutoSuggestBox>().First(b => b.Name == "SearchBox");
            searchBox.Text = "";
            searchButton.Focus(FocusState.Programmatic);
            await Task.Delay(350);

            foreach (var page in new[] { typeof(HomePage), typeof(MadeForYouPage), typeof(BrowsePage), typeof(RadioPage),
                typeof(ArtistsPage), typeof(AlbumsPage), typeof(TracksPage), typeof(PlaylistsPage), typeof(CollectionsPage),
                typeof(FoldersPage), typeof(DownloadsPage), typeof(SearchPage), typeof(MelodayPage), typeof(NowPlayingPage), typeof(TrendingPage) })
                await Page(page);
            await Page(typeof(TrendingPage), "artists");
            if (artist is not null)
            {
                await Page(typeof(ArtistPage), artist);
                await Page(typeof(EssentialsPage), new Playlist { RatingKey = Core.Playlists.Essentials.KeyFor(artist.RatingKey), Title = Core.Playlists.Essentials.TitleFor(artist.Title) });
            }
            if (album is not null) await Page(typeof(AlbumPage), album);
            if (playlist is not null) await Page(typeof(PlaylistPage), playlist);
            if (collection is not null) await Page(typeof(CollectionPage), collection);
            if (tag is not null) { await Page(typeof(TagHubPage), tag); await Page(typeof(TagPage), tag); }
            if (directory is not null) await Page(typeof(StationListPage), directory);
            if (tracks.Count > 0) await Page(typeof(LyricStudioPage), tracks[0]);

            await SettingsSections();
            await Page(typeof(RadioPage));
            await Dialog("artist-builder", () => MixBuilderDialog.ShowAsync(StationKind.ArtistMixBuilder));
            await Dialog("album-builder", () => MixBuilderDialog.ShowAsync(StationKind.AlbumMixBuilder));
            if (tracks.Count > 0)
            {
                await Dialog("sonic-adventure", () => SonicAdventureDialog.ShowAsync(tracks[0]));
                await Dialog("similar-tracks", () => SimilarTracksDialog.ShowAsync(tracks[0]));
                await Dialog("song-info", () => SongInfoDialog.ShowAsync(tracks[0]));
                await Dialog("mute", () => MuteDialog.ShowAsync(tracks[0]));
                await Dialog("duplicates", async () => { await DupeDialog.ShowAsync(tracks[0], tracks); });
                await Dialog("new-playlist", () => PlaylistDialogs.NewAsync(tracks));
            }
            await Dialog("smart-playlist", () => SmartPlaylistDialog.NewAsync());
            if (album is not null) await Dialog("edit-album", async () => { await ItemDetailsDialog.ShowAsync(album); });
            if (artist is not null) await Dialog("edit-artist", async () => { await ItemDetailsDialog.ShowAsync(artist); });
            if (playlist is not null) await Dialog("delete-playlist-cancel", () => PlaylistDialogs.DeleteAsync(playlist));
            foreach (var item in new MediaItem?[] { tracks.FirstOrDefault(), artist, album, playlist, collection, TrendingService.Playlist })
            {
                if (item is null) continue;
                var menu = MediaMenu.Create(item);
                menu.ShowAt(frame.Content as FrameworkElement);
                await Task.Delay(350);
                await Capture("media-menu-" + item.GetType().Name + "-" + (item.RatingKey == TrendingService.PlaylistKey ? "chart" : "library"),
                    VisualTreeHelper.GetOpenPopupsForXamlRoot(Dialogs.Root!).FirstOrDefault(p => p.Child is MenuFlyoutPresenter)?.Child as FrameworkElement);
                menu.Hide();
                notes.Add("Media menu: " + item.GetType().Name + " opened without selecting");
            }
            foreach (var (name, content) in new (string, UIElement)[] {
                ("sign-in", new SignInView()), ("setup", new SetupView()), ("loading", new LoadingView()),
                ("empty-state", new PageStateView { IsEmpty = true, EmptyText = "No music in this library.", EmptyIcon = "Music" }),
                ("error-state", new PageStateView { ErrorMessage = "The library is unavailable.", RetryCommand = null }) })
                await Dialog(name, async () => { var d = Dialogs.Create(name); d.Content = content; d.CloseButtonText = "Close"; await d.ShowAsync(); });
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                window.ApplyTheme(theme);
                await Page(typeof(SettingsPage), "appearance");
                await Capture("theme-" + theme, window.Content as FrameworkElement);
            }
            window.ApplyTheme(settings.Current.Theme);
            await Page(typeof(NowPlayingPage));
            foreach (var control in Walk(window.Content as DependencyObject).OfType<FrameworkElement>().Where(c =>
                c is CuratorButton or CastButton or SleepTimerButton or VisualizerButton or MuteButton).ToList())
            {
                try
                {
                    control.GetType().GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(control, null);
                    var menu = control.GetType().GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(control) as MenuFlyout;
                    if (menu is null) continue;
                    menu.ShowAt(control);
                    await Task.Delay(350);
                    await Capture(control.GetType().Name, VisualTreeHelper.GetOpenPopupsForXamlRoot(Dialogs.Root!).FirstOrDefault(p => p.Child is MenuFlyoutPresenter)?.Child as FrameworkElement);
                    menu.Hide();
                    notes.Add(control.GetType().Name + ": opened and closed without selecting");
                }
                catch (Exception ex) { throw new InvalidOperationException("Popup failed: " + control.GetType().Name, ex); }
            }
            var mini = new MiniPlayerWindow();
            try { mini.Activate(); await Task.Delay(500); await Capture("mini-player", mini.Content as FrameworkElement); }
            finally { mini.Close(); window.Activate(); }
            await Stage();
            await CheckRadioUiAsync();
            notes.Add("POLISH SMOKE PASS: page, Settings, dialog and window traversal completed; no save or playback actions invoked.");
            log.LogInformation("POLISH SMOKE PASS; {Cases} renders/checks in {Output}", notes.Count, output);
        }
        catch (Exception ex) { notes.Add("POLISH SMOKE FAIL: " + ex); log.LogError(ex, "POLISH SMOKE FAIL"); }
        finally
        {
            Helpers.Motion.Set(settings.Current.Motion);
            window.ApplyTheme(settings.Current.Theme);
            if (maximized) presenter?.Maximize();
            else window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(position.X, position.Y, size.Width, size.Height));
            nav.Navigate(originalPage, originalParameter);
            await Task.Delay(650);
            settings.Current.SettingsTab = originalTab;
            settings.Save();
            File.WriteAllLines(Path.Combine(output, "result.txt"), notes);
        }

        async Task IntegrationChecks()
        {
            var session = Services.GetRequiredService<PlexSession>();
            var testSettings = new SettingsStore(Path.Combine(output, "isolated-settings.json"), Services.GetRequiredService<ILogger<SettingsStore>>());
            testSettings.Current.LastFmTrending = testSettings.Current.LastFmTrendingArtists = true;
            var noKey = new LastFmService(new SmokeMemorySecret(), testSettings, new HttpClient(),
                Services.GetRequiredService<PlayerService>(), Services.GetRequiredService<ILogger<LastFmService>>());
            if (noKey.HasApiKey || noKey.IsConnected || noKey.TrendingOn || noKey.TrendingArtistsOn || noKey.ScrobblingOn)
                throw new InvalidOperationException("Last.fm became available without a personal key.");
            if (await noKey.ChartAsync() is not null || (await noKey.ChartPageAsync(1)).Count != 0 || (await noKey.ArtistChartPageAsync(1)).Count != 0)
                throw new InvalidOperationException("No-key chart calls were not disabled.");
            var layout = new HomeLayoutService(testSettings, noKey);
            layout.UpdateCatalog([("library", "Library"), ("trending", "Last.fm Tracks"), ("trending.artists", "Last.fm Artists")]);
            if (layout.Sections.Count != 1) throw new InvalidOperationException("Last.fm Home options are visible without a key.");
            var keyOnlyStore = new SmokeMemorySecret();
            keyOnlyStore.Save(System.Text.Json.JsonSerializer.Serialize(new MelodayMusic.Core.LastFm.LastFmAccount { ApiKey = "test-user-key" }));
            var keyOnly = new LastFmService(keyOnlyStore, testSettings, new HttpClient(), Services.GetRequiredService<PlayerService>(), Services.GetRequiredService<ILogger<LastFmService>>());
            if (!keyOnly.HasApiKey || !keyOnly.TrendingOn || !keyOnly.TrendingArtistsOn || keyOnly.IsConnected || keyOnly.ScrobblingOn)
                throw new InvalidOperationException("Charts incorrectly depend on a scrobbling session.");
            notes.Add("No-key and key-only Last.fm gating passed without sending requests.");
            if (testSettings.Current.MusicBrainzCatalogue || testSettings.Current.ListenBrainzScrobble)
                throw new InvalidOperationException("New optional services were enabled by default.");
            foreach (var width in new[] { 1440, 1024, 720 })
            {
                presenter?.Restore(); window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 900));
                await Page(typeof(SettingsPage), "connections", capture: false);
                await Capture($"connections-{width}", window.Content as FrameworkElement);
                await ScrollContent($"connections-{width}", frame.Content as DependencyObject);
                var groups = Walk(frame.Content as DependencyObject).OfType<StackPanel>().Where(p => p.Name is "LastFmGroup" or "MusicBrainzGroup" or "ListenBrainzGroup").ToList();
                if (groups.Count != 3 || groups.Any(g => g.Visibility != Visibility.Visible || g.ActualHeight == 0))
                    throw new InvalidOperationException("An integration Settings group did not render.");
                notes.Add($"Connections at {width}px: all three integration groups rendered.");
            }
            var catalogue = Services.GetRequiredService<CatalogueService>();
            var original = catalogue.IsOn;
            try
            {
                var artistSeeds = (await session.Client!.GetArtistsAsync(session.Section!.Key, 0, 100)).Items;
                var artist = artistSeeds.FirstOrDefault(a => a.MusicBrainzId is not null)
                    ?? artistSeeds.FirstOrDefault(a => a.Title.Equals("Ariana Grande", StringComparison.OrdinalIgnoreCase)) ?? artistSeeds.FirstOrDefault();
                if (artist is null) throw new InvalidOperationException("No library artist available for the UI check.");
                catalogue.SetEnabled(true);
                presenter?.Restore(); window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 1000));
                await Page(typeof(ArtistPage), artist, capture: false);
                var vm = ((ArtistPage)frame.Content).ViewModel;
                for (var i = 0; i < 150 && vm.CatalogueStatus.StartsWith("Loading"); i++) await Task.Delay(200);
                notes.Add($"Artist catalogue: {vm.NewReleases.Count} recent, {vm.MissingReleases.Count} missing; status={vm.CatalogueStatus}");
                await ScrollContent("artist-catalogue", frame.Content as DependencyObject);
                catalogue.SetEnabled(false); await Task.Delay(300);
                if (vm.CatalogueVisible || vm.NewReleases.Count != 0 || vm.MissingReleases.Count != 0)
                    throw new InvalidOperationException("Disabled catalogue did not disappear immediately.");
                notes.Add("Disabling MusicBrainz clears its artist-page sections.");
            }
            finally { catalogue.SetEnabled(original); }
        }

        async Task CoversAndSpotlight()
        {
            var home = Services.GetRequiredService<MelodayMusic.ViewModels.HomeViewModel>();
            await home.LoadAsync();
            var spotlight = home.Rows.FirstOrDefault(r => r.Identifier == "spotlight");
            if (spotlight is not null && spotlight.CardStyle != MelodayMusic.ViewModels.CardStyle.EditorialBoard)
                throw new InvalidOperationException("Spotlight does not use editorial cards");
            foreach (var width in new[] { 1440, 1024 })
            {
                presenter?.Restore();
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 1000));
                await Page(typeof(HomePage), capture: false);
                var row = Walk(frame.Content as DependencyObject).OfType<MediaRowControl>().FirstOrDefault(r => r.Header == "Spotlight");
                row?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = .25 });
                await Task.Delay(600);
                await Capture($"spotlight-{width}", window.Content as FrameworkElement);
                foreach (var board in Walk(row).OfType<Billboard>())
                {
                    var fields = Walk(board).OfType<FrameworkElement>().ToList();
                    var heading = fields.Single(e => e.Name == "Heading");
                    if (heading.Visibility != Visibility.Visible || board.ArtworkTop < heading.ActualHeight)
                        throw new InvalidOperationException("Spotlight heading overlays its artwork");
                    var title = fields.OfType<TextBlock>().Single(e => e.Name == "HeadingTitle");
                    if (title.MaxLines < 2 || title.TextWrapping == TextWrapping.NoWrap)
                        throw new InvalidOperationException("Spotlight names cannot wrap");
                    if (board.Board?.Item is Album)
                    {
                        var cover = fields.Single(e => e.Name == "Cover");
                        if (cover.Visibility != Visibility.Visible || string.IsNullOrEmpty(board.Board.CoverPath))
                            throw new InvalidOperationException("Album spotlight is missing its cover");
                    }
                }
                notes.Add($"Spotlight {width}: outside heading, two-line names and album corner art");
            }

            var essentials = await EssentialsCover.RenderAsync("qa-0.72.0", "Essentials Example", null, log)
                ?? throw new InvalidOperationException("Essentials render failed");
            var gradient = await PlaylistCover.RenderAsync("qa-0.72.0", "Discovery Example", log)
                ?? throw new InvalidOperationException("Gradient cover render failed");
            using (var bitmap = await Microsoft.Graphics.Canvas.CanvasBitmap.LoadAsync(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(), essentials))
            {
                var pixels = bitmap.GetPixelBytes();
                uint Pixel(int x, int y) { var i = (y * 1000 + x) * 4; return (uint)(pixels[i + 2] << 16 | pixels[i + 1] << 8 | pixels[i]); }
                var ground = Pixel(999, 600);
                if (ground != 0xA6C9BE) throw new InvalidOperationException("Essentials panel changed");
                var matching = 0;
                for (var y = 70; y < 123; y++) for (var x = 70; x < 190; x++) if (Pixel(x, y) == ground) matching++;
                if (matching < 20) throw new InvalidOperationException("Essentials logo does not match the bottom panel");
                var edge = Core.Covers.CoverEdge.Colour(ground);
                if (Pixel(8, 800) != edge || Pixel(500, 990) != edge)
                    throw new InvalidOperationException("Essentials edge colour or placement differs");
            }
            File.Copy(essentials, Path.Combine(output, "essentials.png"), true);
            File.Copy(gradient, Path.Combine(output, "gradient.png"), true);
            notes.Add("Essentials: exact lower-panel symbol colour and both overlay edges verified from saved pixels");

            var artist = spotlight?.Items.OfType<MelodayMusic.ViewModels.BoardItem>().Select(b => b.Item).OfType<Artist>().FirstOrDefault()
                ?? (await Services.GetRequiredService<PlexSession>().Client!.GetArtistsAsync(Services.GetRequiredService<PlexSession>().Section!.Key, 0, 1)).Items.First();
            var mix = new ArtistMix { RatingKey = "mix:artist:" + artist.RatingKey, Title = artist.Title + " Mix", Artist = artist };
            var large = new ColourBoard { Item = mix, BoardWidth = 200 };
            var square = new MixCover { Width = 200, Height = 200, Title = "Focus Radio" };
            var main = new MixArtist(artist.Title, artist.Thumb);
            square.Show(new MixCoverData(new MixInfo("Focus Radio", [main]), main, [], 0x145041));
            var timed = new MelodayArt { Width = 200, Height = 200, Period = "Afternoon", Title = "Timed Example" };
            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            cards.Children.Add(square); cards.Children.Add(large); cards.Children.Add(timed);
            var dialog = new ContentDialog { XamlRoot = Dialogs.Root, Title = "Cover checks", Content = cards, CloseButtonText = "Close" };
            dialog.Resources["ContentDialogMaxWidth"] = 760d;
            var showing = dialog.ShowAsync();
            try
            {
                await Task.Delay(2200);
                if (Walk(large).OfType<CoverEdgeLayer>().Any() || Walk(timed).OfType<CoverEdgeLayer>().Any())
                    throw new InvalidOperationException("Excluded cover type has an edge overlay");
                var symbol = Walk(large).OfType<CoverSymbolLayer>().Single();
                if (symbol.Visibility != Visibility.Visible) throw new InvalidOperationException("Mix colour-board symbol missing");
                var logo = Walk(symbol).OfType<ArtistLogo>().Single();
                var ink = (Microsoft.UI.Composition.CompositionColorBrush)typeof(ArtistLogo).GetField("_colour", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(logo)!;
                var panel = Walk(large).OfType<Border>().Single(b => b.Name == "Panel");
                if (ink.Color != ((SolidColorBrush)panel.Background).Color)
                    throw new InvalidOperationException("Mix logo and lower panel colours differ");
                await Capture("cover-cards", dialog);
                notes.Add("Mix colour-board logo matches panel exactly; square has edges; large board and timed cover have none");
            }
            finally { dialog.Hide(); }
            await showing;
            if (spotlight?.Items.OfType<MelodayMusic.ViewModels.BoardItem>().FirstOrDefault(b => b.Item is Album) is { } albumBoard)
            {
                var longName = new Billboard { IsEditorial = true, BoardWidth = 420,
                    Board = new MelodayMusic.ViewModels.BoardItem { Item = albumBoard.Item, Eyebrow = "New album",
                        Title = "A longer album title with plenty of room for a second line",
                        Subtitle = "An artist name with several featured collaborators", Detail = "2026",
                        ArtPath = albumBoard.ArtPath, CoverPath = albumBoard.CoverPath } };
                var longDialog = new ContentDialog { XamlRoot = Dialogs.Root, Title = "Spotlight wrapping check", Content = longName, CloseButtonText = "Close" };
                var longShowing = longDialog.ShowAsync();
                try
                {
                    await Task.Delay(700);
                    var title = Walk(longName).OfType<TextBlock>().Single(e => e.Name == "HeadingTitle");
                    if (title.ActualHeight < title.FontSize * 1.5) throw new InvalidOperationException("Long Spotlight name did not wrap");
                    await Capture("spotlight-long-name", longDialog);
                    notes.Add("Long Spotlight name: wraps above image; album artwork remains in bottom-left corner");
                }
                finally { longDialog.Hide(); }
                await longShowing;
            }
        }

        async Task RadioCovers()
        {
            var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
            var names = new[] { "Nicki Minaj Mix", "Ariana Grande Mix", "A longer discovery mix with several featured artists" };
            var grounds = new uint[] { 0x2873A5, 0xCD7D8C, 0x145041 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var n = 0; n < names.Length; n++)
            {
                using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(device, 1000, 1000, 96);
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(Microsoft.UI.Colors.Transparent);
                    MelodayCover.DrawLabels(ds, "", names[n], 1000, 1000, log);
                    CoverEdges.Draw(ds, grounds[n]);
                }
                var pixels = target.GetPixelBytes();
                var occupied = Enumerable.Range(700, 250).Where(y => pixels[(y * 1000 + 15) * 4 + 3] > 127).ToArray();
                if (occupied.Length != 100 || occupied.First() != 770 || occupied.Last() != 869)
                    throw new InvalidOperationException("Saved left bar changes with the label: " + names[n]);
                await target.SaveAsync(Path.Combine(output, $"fixed-label-{n}.png"), Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
                var mix = new MixCover { Width = 200, Height = 200, Title = names[n] };
                var main = new MixArtist("Example", null);
                mix.Show(new MixCoverData(new MixInfo(names[n], [main]), main, [], grounds[n]));
                row.Children.Add(mix);
                notes.Add($"Saved cover {names[n]}: fixed left bar at 770..870, 36px thick");
            }
            var dialog = new ContentDialog { XamlRoot = Dialogs.Root, Title = "Fixed cover bars", Content = row, CloseButtonText = "Close" };
            dialog.Resources["ContentDialogMaxWidth"] = 850d;
            var showing = dialog.ShowAsync();
            try
            {
                await Task.Delay(800);
                foreach (var mix in row.Children.OfType<MixCover>())
                {
                    var edge = Walk(mix).OfType<CoverEdgeLayer>().Single();
                    var left = Walk(edge).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().Single(r => r.Width == 36);
                    if (left.Height != 100 || left.Margin.Top != 770) throw new InvalidOperationException("Native left bar varies with title");
                }
                await Capture("native-fixed-bars", dialog);
                notes.Add("Native covers: single-line, wrapped and fitted titles all retain the same bar geometry");
            }
            finally { dialog.Hide(); }
            await showing;
        }

        async Task PlayerLayout()
        {
            var bar = Walk(window.Content as DependencyObject).OfType<PlayerBar>().First();
            var elements = Walk(bar).OfType<FrameworkElement>().ToList();
            var badge = elements.Single(e => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == "LiveBadge");
            var programme = elements.OfType<TextBlock>().Single(e => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == "PulseProgramme");
            var title = elements.Single(e => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(e) == "NowPlayingTitle");
            var seek = elements.Single(e => e.Name == "ProgressSlider");
            var artist = elements.OfType<HyperlinkButton>().Single(e => Grid.GetRow(e) == 1);
            var time = elements.Single(e => e.Name == "TimeText");
            var curator = elements.OfType<CuratorButton>().Single();
            var originalBadge = badge.Visibility;
            var originalProgramme = programme.Visibility;
            var originalText = programme.Text;
            var originalTime = time.Visibility;
            var originalCurator = curator.Visibility;
            var originalChecked = curator.IsChecked;
            try
            {
                foreach (var width in new[] { 1920, 1440, 1024, 800, 720 })
                {
                    presenter?.Restore();
                    window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 900));
                    await Task.Delay(550);
                    time.Visibility = badge.Visibility = Visibility.Collapsed;
                    bar.UpdateLayout();
                    if (title.ActualHeight <= 0 || bar.ActualWidth <= 0) throw new InvalidOperationException("Player layout did not settle");
                    double Y(FrameworkElement e) => e.TransformToVisual(bar).TransformPoint(new Windows.Foundation.Point()).Y;
                    var baseline = new[] { Y(title), Y(artist), Y(seek) };
                    foreach (var label in new[] { "", "Chart Pulse", "An exceptionally long artist takeover programme" })
                    {
                        badge.Visibility = Visibility.Visible;
                        programme.Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                        programme.Text = label;
                        bar.UpdateLayout();
                        var actual = new[] { Y(title), Y(artist), Y(seek) };
                        if (actual.Zip(baseline).Any(pair => Math.Abs(pair.First - pair.Second) > .5))
                            throw new InvalidOperationException($"Live label shifts player rows at {width}: {label}");
                        await Task.Delay(150);
                        await Capture($"player-{width}-{(label.Length == 0 ? "station" : label == "Chart Pulse" ? "programme" : "long")}", window.Content as FrameworkElement);
                    }
                    badge.Visibility = Visibility.Collapsed;
                    curator.Visibility = Visibility.Visible;
                    curator.IsChecked = true;
                    bar.UpdateLayout();
                    var dot = Walk(curator).OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().Single(e => e.Name == "Dot");
                    var icon = Walk(curator).OfType<LucideIcon>().Single();
                    var dotY = dot.TransformToVisual(curator).TransformPoint(new Windows.Foundation.Point()).Y;
                    var iconY = icon.TransformToVisual(curator).TransformPoint(new Windows.Foundation.Point()).Y;
                    var gap = dotY - iconY - icon.ActualHeight;
                    if (gap < 4) throw new InvalidOperationException($"Curator dot too close at {width}: {gap:F1}px");
                    await Task.Delay(150);
                    await Capture($"player-{width}-curator", window.Content as FrameworkElement);
                    notes.Add($"Player {width}: three live label states preserve row positions; curator gap {gap:F1}px");
                    curator.Visibility = originalCurator;
                    curator.IsChecked = originalChecked;
                }
            }
            finally
            {
                badge.Visibility = originalBadge;
                programme.Visibility = originalProgramme;
                programme.Text = originalText;
                time.Visibility = originalTime;
                curator.Visibility = originalCurator;
                curator.IsChecked = originalChecked;
            }
        }

        async Task Page(Type type, object? parameter = null, bool capture = true)
        {
            nav.Navigate(type, parameter);
            await Task.Delay(750);
            if (nav.CurrentPage != type) throw new InvalidOperationException("Navigation failed: " + type.Name);
            var vm = type.GetProperty("ViewModel")?.GetValue(frame.Content);
            for (var wait = 0; wait < 40 && vm?.GetType().GetProperty("IsLoading")?.GetValue(vm) is true; wait++) await Task.Delay(250);
            frame.UpdateLayout();
            if (capture) await Capture(type.Name + (parameter is string s ? "-" + s : ""), window.Content as FrameworkElement);
            if (capture)
            {
                await ScrollContent(type.Name, frame.Content as DependencyObject);
                await Task.Delay(400);
                frame.UpdateLayout();
                await Capture(type.Name + (parameter is string key ? "-" + key : "") + "-ready", window.Content as FrameworkElement);
            }
            notes.Add(type.Name + ": opened");
        }

        async Task SettingsSections()
        {
            foreach (var width in new[] { 1440, 1024, 720 })
            {
                presenter?.Restore();
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 900));
                await Task.Delay(600);
                await Page(typeof(SettingsPage), "general", capture: false);
                var sectionNav = Walk(frame.Content as DependencyObject).OfType<ListView>().First(l => l.Name == "SectionNav");
                if (sectionNav.Items.Count != 8) throw new InvalidOperationException("Expected eight Settings sections");
                foreach (var tab in sectionNav.Items.Cast<ChipTab>())
                {
                    sectionNav.SelectedItem = tab;
                    await Task.Delay(650);
                    frame.UpdateLayout();
                    await Capture($"settings-{width}-{tab.Key}", window.Content as FrameworkElement);
                    await ScrollContent($"settings-{width}-{tab.Key}", frame.Content as DependencyObject);
                    notes.Add($"Settings {width}/{tab.Key}: opened");
                }
            }
        }

        async Task Stage()
        {
            var service = Services.GetRequiredService<StageService>();
            var visualizer = Services.GetRequiredService<VisualizerService>();
            var originalStyle = visualizer.Style;
            var player = Services.GetRequiredService<PlayerService>();
            var wasPlaying = player.IsPlaying;
            var wasOn = service.IsOn;
            try
            {
                service.Enter();
                await Task.Delay(800);
                await Capture("visualizer-stage", window.Content as FrameworkElement);
                notes.Add("Full-screen visualizer: opened actual layer and rendered controls");
            }
            finally
            {
                if (!wasOn) service.Exit();
                visualizer.Choose(originalStyle);
                await Task.Delay(800);
                if (player.IsPlaying != wasPlaying) throw new InvalidOperationException("Visualizer check changed playback state");
            }
        }

        async Task Dialog(string name, Func<Task> open)
        {
            ContentDialog? dialog = null;
            async Task Observe()
            {
                for (var wait = 0; wait < 300 && dialog is null; wait++)
                {
                    await Task.Delay(100);
                    dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Dialogs.Root!).SelectMany(p => Walk(p.Child)).OfType<ContentDialog>().FirstOrDefault();
                }
                if (dialog is null) throw new InvalidOperationException("Dialog did not open: " + name);
                try
                {
                    await Task.Delay(1200); dialog.UpdateLayout(); await Capture(name, dialog);
                    if (name == "smart-playlist")
                    {
                        var add = Walk(dialog).OfType<Button>().FirstOrDefault(b => b.Content as string == "Add Rule");
                        if (add is not null) new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(add).Invoke();
                        await Task.Delay(600);
                        await Capture(name + "-rules", dialog);
                    }
                    if (name == "duplicates")
                    {
                        var list = Walk(dialog).OfType<ListView>().First(l => l.Items.Count > 0);
                        list.SelectedIndex = 0;
                        var next = Walk(dialog).OfType<Button>().First(b => b.Name == "PrimaryButton");
                        new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(next).Invoke();
                        await Task.Delay(400);
                        await Capture(name + "-choose", dialog);
                    }
                    if (name.StartsWith("edit-"))
                    {
                        var tabs = Walk(dialog).OfType<ChipTabs>().FirstOrDefault();
                        if (tabs?.ItemsSource is IEnumerable<ChipTab> entries)
                            foreach (var tab in entries)
                            {
                                tabs.SelectedItem = tab;
                                await Task.Delay(700);
                                await Capture(name + "-" + tab.Key, dialog);
                            }
                    }
                    await ScrollContent(name, dialog, dialog);
                }
                finally { dialog.Hide(); }
                notes.Add(name + ": opened, rendered and cancelled");
            }
            await Task.WhenAll(Observe(), open()).WaitAsync(TimeSpan.FromSeconds(40));
        }

        async Task Capture(string name, FrameworkElement? element)
        {
            if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) { notes.Add(name + ": no rendered content"); return; }
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            var pixels = await bitmap.GetPixelsAsync();
            using var stream = File.Open(Path.Combine(output, name + ".png"), FileMode.Create);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
            await encoder.FlushAsync();
        }

        async Task ScrollContent(string name, DependencyObject? root, FrameworkElement? capture = null)
        {
            var scroller = Walk(root).OfType<ScrollViewer>().FirstOrDefault(s => s.ScrollableHeight > 1 && s.ActualHeight > 100);
            if (scroller is null) return;
            var original = scroller.VerticalOffset;
            var height = scroller.ScrollableHeight;
            var count = Math.Min(root is SettingsPage ? 12 : 4, Math.Max(1, (int)Math.Ceiling(height / (scroller.ViewportHeight * .8))));
            for (var step = 1; step <= count; step++)
            {
                var offset = height * step / count;
                scroller.ChangeView(null, offset, null, true);
                await Task.Delay(160);
                await Capture(name + "-scroll-" + (int)offset, capture ?? window.Content as FrameworkElement);
            }
            scroller.ChangeView(null, original, null, true);
        }
    }

    private sealed class SmokeMemorySecret : MelodayMusic.Core.Security.ISecretStore
    {
        private string? _value;
        public string? Load() => _value;
        public void Save(string value) => _value = value;
        public void Clear() => _value = null;
        public bool HasSecret => _value is not null;
    }
}

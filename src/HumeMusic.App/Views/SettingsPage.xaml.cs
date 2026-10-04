using HumeMusic.Controls;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Layout = App.Services.GetRequiredService<Services.HomeLayoutService>();
        InitializeComponent();
        SetUpSections();
    }

    // ------------------------------------------------------------------ sections

    private static readonly ChipTab[] Sections =
    [
        new("general", "General", "Palette"),
        new("home", "Home", "House"),
        new("plex", "Plex", "Server"),
        new("playback", "Playback", "AudioWaveform"),
        new("meloday", "Meloday", "Sparkles"),
        new("artists", "Artists", "MicVocal"),
        new("about", "About", "Info"),
    ];

    /// <summary>Settings shows one section at a time (the last one open is remembered).</summary>
    private void SetUpSections()
    {
        var settings = App.Services.GetRequiredService<Core.Settings.SettingsStore>();
        SectionTabs.ItemsSource = Sections;
        var open = Sections.FirstOrDefault(t => t.Key == settings.Current.SettingsTab) ?? Sections[0];
        SectionTabs.SelectedItem = open;
        ShowSection(open.Key, resetScroll: false);
        SectionTabs.RegisterPropertyChangedCallback(ChipTabs.SelectedItemProperty, (_, _) =>
        {
            if (SectionTabs.SelectedItem is not ChipTab tab) return;
            ShowSection(tab.Key, resetScroll: true);
            settings.Current.SettingsTab = tab.Key;
            settings.Save();
        });
    }

    private void ShowSection(string key, bool resetScroll)
    {
        void Show(UIElement group, params string[] sections) =>
            group.Visibility = sections.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
        Show(AppearanceGroup, "general");
        Show(HomeGroup, "home");
        Show(AccountGroup, "plex");
        Show(PlexPassGroup, "plex");
        Show(LibraryGroup, "plex");
        Show(PlaybackGroup, "playback");
        Show(MelodayGroup, "meloday");
        Show(ArtistGroup, "artists");
        Show(AboutGroup, "about");
        if (resetScroll) SettingsScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    public SettingsViewModel ViewModel { get; }

    public Services.HomeLayoutService Layout { get; }

    // The key goes straight from the box to the encrypted store; the box is cleared either way.
    private async void OnSaveFanartKey(object sender, RoutedEventArgs e)
    {
        var key = FanartKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (await ViewModel.SaveLogoKeyAsync(key)) FanartKeyBox.Password = string.Empty;
    }

    // As with fanart.tv: the key goes straight from the box to the encrypted store.
    private async void OnImportLogos(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window) return;
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
        };
        if (await picker.PickSingleFolderAsync() is { Path: { Length: > 0 } folder }) await ViewModel.ImportLogosAsync(folder);
    }

    private async void OnSaveOpenAiKey(object sender, RoutedEventArgs e)
    {
        var key = OpenAiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (await ViewModel.SaveAiKeyAsync(key)) OpenAiKeyBox.Password = string.Empty;
    }

    private async void OnConnectLastFm(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.ConnectLastFmAsync(LastFmKeyBox.Password, LastFmSecretBox.Password))
        {
            LastFmKeyBox.Password = string.Empty;
            LastFmSecretBox.Password = string.Empty;
        }
    }

    private void OnOpenAiKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnSaveOpenAiKey(sender, e);
    }

    private void OnSkipGenresDone(object sender, RoutedEventArgs e) => ViewModel.SaveMelodaySkipGenres();

    private void OnSkipGenresKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.SaveMelodaySkipGenres();
    }

    private void OnAvoidWordsDone(object sender, RoutedEventArgs e) => ViewModel.SaveMelodayAvoidWords();

    private void OnAvoidWordsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.SaveMelodayAvoidWords();
    }

    private void OnFanartKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnSaveFanartKey(sender, e);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSection(sender, -1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSection(sender, 1);

    private void MoveSection(object sender, int direction)
    {
        if ((sender as FrameworkElement)?.Tag is not Services.HomeSection section) return;
        var index = Layout.Sections.IndexOf(section);
        Layout.Move(index, index + direction);
    }

    // A drag reorder has already moved the item in the list; save the new order.
    private void OnSectionsReordered(ListViewBase sender, DragItemsCompletedEventArgs args) => Layout.SaveOrder();

    private void OnResetHome(object sender, RoutedEventArgs e) => Layout.Reset();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
    }
}

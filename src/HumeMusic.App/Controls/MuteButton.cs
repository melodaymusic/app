using HumeMusic.Core.Models;
using HumeMusic.Core.Mutes;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HumeMusic.Controls;

/// <summary>
/// Now Playing's Mute: keeps the song on, or its artist, out of everything Meloday makes for a while, without rating them down.
/// A capsule that says what it does ("Mute", or "Muted" while the song or its artist is), opening a menu to mute the song or the
/// artist for the length set in Settings, for another length, or to let them back.
/// </summary>
public sealed partial class MuteButton : Button
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly MuteService _mutes = App.Services.GetRequiredService<MuteService>();
    private readonly IconLabel _label = new() { Icon = "BellOff", Text = "Mute" };

    public MuteButton()
    {
        Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"];
        Content = _label;
        AutomationProperties.SetAutomationId(this, "MuteButton");
        Click += (_, _) => ShowMenu();
        Loaded += (_, _) =>
        {
            _player.PropertyChanged += OnPlayerChanged;
            _mutes.Changed += OnMutesChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _mutes.Changed -= OnMutesChanged;
        };
    }

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerService.CurrentTrack)) DispatcherQueue.TryEnqueue(Update);
    }

    private void OnMutesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Update);

    /// <summary>"Mute" until the song or its artist is muted, then "Muted", with when it ends said in the tooltip.</summary>
    private void Update()
    {
        var track = _player.CurrentTrack;
        IsEnabled = track is not null;
        var song = track is null ? null : _mutes.SongMute(track);
        var artist = track is null ? null : _mutes.ArtistMute(track);
        var muted = song ?? artist;
        _label.Text = muted is null ? "Mute" : "Muted";
        var tip = muted is null
            ? "Keep this song or its artist out of Meloday for a while"
            : $"{(song is not null ? "This song" : artist!.Title)} is muted · {MuteLengths.Remaining(muted.Until, DateTimeOffset.UtcNow)}";
        ToolTipService.SetToolTip(this, tip);
        AutomationProperties.SetName(this, muted is null ? "Mute" : $"Muted. {tip}");
    }

    private void ShowMenu()
    {
        if (_player.CurrentTrack is not { } track) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Top };
        var now = DateTimeOffset.UtcNow;

        void Add(string text, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        // The song, then its artist: each is muted for the length set in Settings, or let back.
        if (_mutes.SongMute(track) is { } song)
        {
            Add($"This song is muted · {MuteLengths.Remaining(song.Until, now)}", () => { }, enabled: false);
            Add("Unmute Song", () => _mutes.Unmute(song));
        }
        else Add($"Mute Song for {_mutes.DefaultText}", () => _ = _mutes.MuteSongAsync(track));

        if (track.ArtistRatingKey is not null)
        {
            var name = track.ArtistTitle ?? track.DisplayArtist;
            menu.Items.Add(new MenuFlyoutSeparator());
            if (_mutes.ArtistMute(track) is { } artist)
            {
                Add($"{name} is muted · {MuteLengths.Remaining(artist.Until, now)}", () => { }, enabled: false);
                Add($"Unmute {name}", () => _mutes.Unmute(artist));
            }
            else Add($"Mute {name} for {_mutes.DefaultText}", () => _ = _mutes.MuteArtistAsync(track));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Another Length…", () => _ = Views.MuteDialog.ShowAsync(track));
        Add("Muted Music…", () => App.Services.GetRequiredService<LibraryActions>().OpenMutedMusic());
        menu.ShowAt(this);
    }
}

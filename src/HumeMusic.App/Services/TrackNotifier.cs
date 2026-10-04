using System.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace HumeMusic.Services;

/// <summary>
/// A quiet notification as each song starts, with its artwork, while Meloday Music is in the
/// background (when the window is in use, the song is already in view). Each replaces the last, so
/// the notification centre holds only what's playing now.
/// </summary>
public sealed class TrackNotifier
{
    private const string Tag = "now-playing";
    private const string Group = "playback";

    private readonly PlayerService _player;
    private readonly SettingsStore _settings;
    private readonly ArtworkService _artwork;
    private readonly ILogger<TrackNotifier> _log;
    private bool _registered;
    private string? _lastKey;

    public TrackNotifier(PlayerService player, SettingsStore settings, ArtworkService artwork, ILogger<TrackNotifier> log)
    {
        _player = player;
        _settings = settings;
        _artwork = artwork;
        _log = log;
        _player.PropertyChanged += OnPlayerChanged;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerService.IsPlaying) || !_player.IsPlaying) return;
        if (_player.CurrentTrack is not { } track || track.RatingKey == _lastKey) return;
        _lastKey = track.RatingKey;
        if (!_settings.Current.TrackNotifications || Helpers.Motion.IsWindowActive) return;
        _ = ShowAsync(track);
    }

    private async Task ShowAsync(Track track)
    {
        try
        {
            if (!_registered)
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            var builder = new AppNotificationBuilder()
                .AddText(track.Title)
                .AddText(string.IsNullOrEmpty(track.AlbumTitle) ? track.DisplayArtist : $"{track.DisplayArtist} — {track.AlbumTitle}")
                .MuteAudio();
            if (await _artwork.GetLocalPathAsync(track.DisplayThumb, 200) is { } art && File.Exists(art))
                builder.SetAppLogoOverride(new Uri(art), AppNotificationImageCrop.Default);
            var notification = builder.BuildNotification();
            notification.Tag = Tag;
            notification.Group = Group;
            notification.ExpiresOnReboot = true;
            notification.Expiration = DateTimeOffset.Now.AddMinutes(30);
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(Tag, Group);
            AppNotificationManager.Default.Show(notification);
            _log.LogDebug("Notified: {Track}", track.Title);
        }
        catch (Exception ex)
        {
            // Notifications switched off for the app, or not available: nothing to show.
            _log.LogDebug("Couldn't show the song notification: {Message}", ex.Message);
        }
    }

    /// <summary>Takes the notification away (as the app closes).</summary>
    public void Clear()
    {
        if (!_registered) return;
        try
        {
            _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(Tag, Group);
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            _log.LogDebug("Couldn't clear notifications: {Message}", ex.Message);
        }
    }
}

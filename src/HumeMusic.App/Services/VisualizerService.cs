using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Playback;
using HumeMusic.Core.Settings;

namespace HumeMusic.Services;

/// <summary>
/// Which picture Now Playing draws from the music (see <see cref="VisualizerStyle"/>), kept as a setting. The pictures
/// are drawn from what this computer's own mixer plays, so choosing one while Windows' player has the song moves it
/// to the mixer at once (from where it is) rather than at the next song.
/// </summary>
public sealed partial class VisualizerService : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly PlayerService _player;

    public VisualizerService(SettingsStore settings, PlayerService player)
    {
        _settings = settings;
        _player = player;
        Style = settings.Current.Visualizer;
    }

    [ObservableProperty] public partial VisualizerStyle Style { get; private set; }

    public void Choose(VisualizerStyle style)
    {
        if (style == Style) return;
        Style = style;
        _settings.Current.Visualizer = style;
        _settings.Save();
        if (style != VisualizerStyle.Off && !_player.CanVisualize && _player.CurrentTrack is not null)
            _ = _player.ResumeHereAsync(_player.Position, _player.IsPlaying);
    }
}

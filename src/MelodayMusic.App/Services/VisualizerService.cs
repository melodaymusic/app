using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Playback;
using MelodayMusic.Core.Settings;

namespace MelodayMusic.Services;

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

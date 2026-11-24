using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MelodayMusic.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "meloday-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Motion_is_on_by_default()
    {
        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        Assert.True(store.Current.Motion);
    }

    [Fact]
    public void Motion_off_is_remembered()
    {
        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        store.Current.Motion = false;
        store.Save();

        var reloaded = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        Assert.False(reloaded.Current.Motion);
    }

    [Theory]
    [InlineData("\"LateSpin\"")]
    [InlineData("\"Groovebox\"")]
    [InlineData("21")]
    public void A_visualizer_that_was_removed_falls_back_to_off_and_keeps_the_other_settings(string saved)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, $$"""{ "ClientIdentifier": "meloday-test", "Volume": 0.5, "Visualizer": {{saved}} }""");

        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);

        Assert.Equal(Playback.VisualizerStyle.Off, store.Current.Visualizer);
        Assert.Equal("meloday-test", store.Current.ClientIdentifier);
        Assert.Equal(0.5, store.Current.Volume);
    }

    [Fact]
    public void A_chosen_visualizer_is_remembered_by_name()
    {
        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        store.Current.Visualizer = Playback.VisualizerStyle.Tiles;
        store.Save();

        Assert.Contains("\"Visualizer\": \"Tiles\"", File.ReadAllText(SettingsPath));
        Assert.Equal(Playback.VisualizerStyle.Tiles, new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance).Current.Visualizer);
    }

    [Fact]
    public void Settings_saved_before_the_motion_setting_keep_motion_on()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{ "ClientIdentifier": "meloday-test", "Volume": 0.5 }""");

        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        Assert.True(store.Current.Motion);
        Assert.Equal(0.5, store.Current.Volume);
    }
}

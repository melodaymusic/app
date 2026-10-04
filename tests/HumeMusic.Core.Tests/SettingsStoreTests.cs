using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace HumeMusic.Core.Tests;

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

    [Fact]
    public void Settings_saved_before_the_motion_setting_keep_motion_on()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{ "ClientIdentifier": "hume-test", "Volume": 0.5 }""");

        var store = new SettingsStore(SettingsPath, NullLogger<SettingsStore>.Instance);
        Assert.True(store.Current.Motion);
        Assert.Equal(0.5, store.Current.Volume);
    }
}

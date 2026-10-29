using CommunityToolkit.Mvvm.ComponentModel;

namespace MelodayMusic.Services;

public enum SidePanel
{
    None,
    Queue,
    Lyrics,
}

public sealed partial class PanelService : ObservableObject
{
    [ObservableProperty] public partial SidePanel Open { get; set; }

    public void Toggle(SidePanel panel) => Open = Open == panel ? SidePanel.None : panel;
}

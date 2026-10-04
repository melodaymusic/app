using CommunityToolkit.Mvvm.ComponentModel;

namespace HumeMusic.Services;

/// <summary>The panels that open at the window's right: the queue and the lyrics (one at a time).</summary>
public enum SidePanel
{
    None,
    Queue,
    Lyrics,
}

/// <summary>
/// Which side panel is open. The player bar's buttons, Now Playing and the keyboard all ask here, and
/// the shell shows what it says, so every button's lit state always matches the screen.
/// </summary>
public sealed partial class PanelService : ObservableObject
{
    [ObservableProperty] public partial SidePanel Open { get; set; }

    /// <summary>Opens the panel, or closes it when it is already the one open.</summary>
    public void Toggle(SidePanel panel) => Open = Open == panel ? SidePanel.None : panel;
}

using System.Globalization;
using System.Xml.Linq;

namespace HumeMusic.Core.Companion;

/// <summary>
/// What the player is doing, as Plex's remote-control (Companion) protocol reports it to the apps
/// controlling it: the state, the song and where it is, and the player's settings.
/// </summary>
public sealed record CompanionTimeline
{
    /// <summary>"playing", "paused", "buffering" or "stopped".</summary>
    public string State { get; init; } = "stopped";
    public long TimeMs { get; init; }
    public long DurationMs { get; init; }
    public string? RatingKey { get; init; }
    public string? ContainerKey { get; init; }
    public long? PlayQueueId { get; init; }
    public string? MachineIdentifier { get; init; }
    public string? Address { get; init; }
    public int? Port { get; init; }
    public string? Protocol { get; init; }
    public int Volume { get; init; } = 100;
    public bool Shuffle { get; init; }
    /// <summary>0 off, 1 one, 2 all (Plex's numbering).</summary>
    public int Repeat { get; init; }

    public static readonly CompanionTimeline Stopped = new();

    /// <summary>What a controller can ask of this player while a song plays.</summary>
    public const string Controllable = "playPause,stop,volume,shuffle,repeat,seekTo,skipPrevious,skipNext,stepBack,stepForward";

    /// <summary>
    /// The timeline document: the music timeline, and stopped video and photo timelines (controllers
    /// expect all three).
    /// </summary>
    public XDocument ToXml(int commandId)
    {
        var music = new XElement("Timeline",
            new XAttribute("type", "music"),
            new XAttribute("state", State),
            new XAttribute("controllable", Controllable),
            new XAttribute("volume", Volume),
            new XAttribute("shuffle", Shuffle ? 1 : 0),
            new XAttribute("repeat", Repeat),
            new XAttribute("mute", 0));
        if (State != "stopped" && RatingKey is not null)
        {
            music.Add(
                new XAttribute("time", TimeMs),
                new XAttribute("duration", DurationMs),
                new XAttribute("ratingKey", RatingKey),
                new XAttribute("key", "/library/metadata/" + RatingKey),
                new XAttribute("seekRange", "0-" + DurationMs.ToString(CultureInfo.InvariantCulture)));
            if (ContainerKey is not null) music.Add(new XAttribute("containerKey", ContainerKey));
            if (PlayQueueId is { } id) music.Add(new XAttribute("playQueueID", id));
            if (MachineIdentifier is not null) music.Add(new XAttribute("machineIdentifier", MachineIdentifier));
            if (Address is not null) music.Add(new XAttribute("address", Address));
            if (Port is { } port) music.Add(new XAttribute("port", port));
            if (Protocol is not null) music.Add(new XAttribute("protocol", Protocol));
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("MediaContainer",
                new XAttribute("commandID", commandId),
                new XAttribute("location", State == "stopped" ? "navigation" : "fullScreenMusic"),
                music,
                new XElement("Timeline", new XAttribute("type", "video"), new XAttribute("state", "stopped")),
                new XElement("Timeline", new XAttribute("type", "photo"), new XAttribute("state", "stopped"))));
    }

    /// <summary>Whether a controller would see a change from <paramref name="other"/> (beyond time passing).</summary>
    public bool DiffersFrom(CompanionTimeline other) =>
        State != other.State || RatingKey != other.RatingKey || Volume != other.Volume || Shuffle != other.Shuffle ||
        Repeat != other.Repeat || Math.Abs(TimeMs - other.TimeMs) > 2500;
}

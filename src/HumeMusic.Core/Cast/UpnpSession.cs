using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace HumeMusic.Core.Cast;

/// <summary>
/// Plays to a UPnP/DLNA media renderer (Sonos, many smart TVs and network speakers) through its
/// AVTransport and RenderingControl services: SOAP calls to set the track, play, pause, seek and set
/// the volume, and polled for its state and position (renderers don't push status unasked).
/// </summary>
public sealed class UpnpSession : ICastSession
{
    private const string Transport = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string Rendering = "urn:schemas-upnp-org:service:RenderingControl:1";

    private readonly HttpClient _http;
    private TimeSpan _duration;
    private bool _played;

    public UpnpSession(CastDevice device, HttpClient http)
    {
        Device = device;
        _http = http;
    }

    public CastDevice Device { get; }

    public async Task<bool> LoadAsync(CastMedia media, TimeSpan start, bool autoPlay, CancellationToken ct)
    {
        _duration = media.Duration;
        _played = false;
        await CallAsync(Transport, "Stop", [], ct, quiet: true).ConfigureAwait(false);
        var ok = await CallAsync(Transport, "SetAVTransportURI",
            [("CurrentURI", media.Url.ToString()), ("CurrentURIMetaData", Didl(media))], ct).ConfigureAwait(false);
        if (!ok) return false;
        if (start > TimeSpan.FromSeconds(1)) await SeekAsync(start, ct).ConfigureAwait(false);
        if (autoPlay) await PlayAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task PlayAsync(CancellationToken ct)
    {
        _played = true;
        await CallAsync(Transport, "Play", [("Speed", "1")], ct).ConfigureAwait(false);
    }

    public Task PauseAsync(CancellationToken ct) => CallAsync(Transport, "Pause", [], ct);
    public Task StopAsync(CancellationToken ct) => CallAsync(Transport, "Stop", [], ct, quiet: true);

    public Task SeekAsync(TimeSpan position, CancellationToken ct) =>
        CallAsync(Transport, "Seek", [("Unit", "REL_TIME"), ("Target", Clock(position))], ct);

    public Task SetVolumeAsync(double volume, CancellationToken ct) =>
        Device.VolumeUrl is null ? Task.CompletedTask : CallAsync(Rendering, "SetVolume",
            [("Channel", "Master"), ("DesiredVolume", ((int)Math.Round(Math.Clamp(volume, 0, 1) * 100)).ToString(CultureInfo.InvariantCulture))], ct);

    public async Task<CastStatus?> GetStatusAsync(CancellationToken ct)
    {
        var info = await QueryAsync(Transport, "GetTransportInfo", ct).ConfigureAwait(false);
        var position = await QueryAsync(Transport, "GetPositionInfo", ct).ConfigureAwait(false);
        if (info is null) return null;
        var at = Parse(position?.GetValueOrDefault("RelTime"));
        var length = Parse(position?.GetValueOrDefault("TrackDuration"));
        var duration = length > TimeSpan.Zero ? length : _duration;
        var state = info.GetValueOrDefault("CurrentTransportState") switch
        {
            "PLAYING" => CastState.Playing,
            "PAUSED_PLAYBACK" => CastState.Paused,
            "TRANSITIONING" => CastState.Buffering,
            // Stopped after playing, at (or near) the end: the track finished.
            "STOPPED" or "NO_MEDIA_PRESENT" when _played && (at == TimeSpan.Zero || duration - at < TimeSpan.FromSeconds(3)) => CastState.Finished,
            _ => CastState.Idle,
        };
        return new CastStatus(state, at, duration);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ------------------------------------------------------------------ SOAP

    private async Task<bool> CallAsync(string service, string action, (string Name, string Value)[] args, CancellationToken ct, bool quiet = false) =>
        await SendAsync(service, action, args, ct).ConfigureAwait(false) is not null || quiet;

    private async Task<Dictionary<string, string>?> QueryAsync(string service, string action, CancellationToken ct)
    {
        var body = await SendAsync(service, action, [], ct).ConfigureAwait(false);
        return body?.Descendants().Where(e => !e.HasElements).GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value);
    }

    private async Task<XElement?> SendAsync(string service, string action, (string Name, string Value)[] args, CancellationToken ct)
    {
        var url = service == Rendering ? Device.VolumeUrl : Device.ControlUrl;
        if (url is null) return null;
        var arguments = new StringBuilder("<InstanceID>0</InstanceID>");
        foreach (var (name, value) in args) arguments.Append('<').Append(name).Append('>').Append(WebUtility.HtmlEncode(value)).Append("</").Append(name).Append('>');
        var envelope =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            $"<s:Body><u:{action} xmlns:u=\"{service}\">{arguments}</u:{action}></s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(envelope, Encoding.UTF8, "text/xml") };
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service}#{action}\"");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            return xml.Descendants().FirstOrDefault(e => e.Name.LocalName == action + "Response");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>The track as DIDL-Lite, which renderers (Sonos especially) need to show and play it.</summary>
    private static string Didl(CastMedia media)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var art = media.Artwork is { } a ? $"<upnp:albumArtURI>{E(a.ToString())}</upnp:albumArtURI>" : string.Empty;
        return "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
               "xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">" +
               "<item id=\"1\" parentID=\"0\" restricted=\"1\">" +
               $"<dc:title>{E(media.Title)}</dc:title><dc:creator>{E(media.Artist)}</dc:creator>" +
               $"<upnp:artist>{E(media.Artist)}</upnp:artist><upnp:album>{E(media.Album)}</upnp:album>{art}" +
               "<upnp:class>object.item.audioItem.musicTrack</upnp:class>" +
               $"<res protocolInfo=\"http-get:*:{media.ContentType}:*\" duration=\"{Clock(media.Duration)}\">{E(media.Url.ToString())}</res>" +
               "</item></DIDL-Lite>";
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    private static TimeSpan Parse(string? clock) =>
        clock is not null && TimeSpan.TryParse(clock.Split('.')[0], CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace HumeMusic.Core.Cast;

/// <summary>
/// Finds the devices on the local network that Meloday Music can play to: Google Cast devices by
/// mDNS (their friendly name and model come from the service's TXT record) and UPnP/DLNA media
/// renderers by SSDP (name, model and control addresses from each device's description). Only asks;
/// nothing is sent to a device until the listener picks it.
/// </summary>
public static class CastDiscovery
{
    private static readonly IPEndPoint Mdns = new(IPAddress.Parse("224.0.0.251"), 5353);
    private static readonly IPEndPoint Ssdp = new(IPAddress.Parse("239.255.255.250"), 1900);
    private const string CastService = "_googlecast._tcp.local";

    public static async Task<IReadOnlyList<CastDevice>> FindAsync(HttpClient http, TimeSpan listen, CancellationToken ct = default)
    {
        var cast = FindCastAsync(listen, ct);
        var upnp = FindUpnpAsync(http, listen, ct);
        await Task.WhenAll(cast, upnp).ConfigureAwait(false);
        return cast.Result.Concat(upnp.Result)
            .GroupBy(d => d.Id).Select(g => g.First())
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------ Google Cast (mDNS)

    private static async Task<IReadOnlyList<CastDevice>> FindCastAsync(TimeSpan listen, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
        var query = Dns.Query(CastService, Dns.Ptr);
        try { await udp.SendAsync(query, Mdns, ct).ConfigureAwait(false); }
        catch (SocketException) { return []; }

        var records = new List<Dns.Record>();
        await ReceiveAsync(udp, listen, ct, (data, _) => records.AddRange(Dns.Parse(data))).ConfigureAwait(false);

        var devices = new List<CastDevice>();
        foreach (var ptr in records.Where(r => r.Type == Dns.Ptr && r.Name.Equals(CastService, StringComparison.OrdinalIgnoreCase)))
        {
            var instance = ptr.Target!;
            var srv = records.FirstOrDefault(r => r.Type == Dns.Srv && r.Name.Equals(instance, StringComparison.OrdinalIgnoreCase));
            var txt = records.FirstOrDefault(r => r.Type == Dns.Txt && r.Name.Equals(instance, StringComparison.OrdinalIgnoreCase))?.Text ?? [];
            var host = srv?.Target is { } target
                ? records.FirstOrDefault(r => r.Type == Dns.A && r.Name.Equals(target, StringComparison.OrdinalIgnoreCase))?.Address
                : null;
            host ??= ptr.From;
            if (host is null) continue;
            var name = txt.GetValueOrDefault("fn") ?? instance.Split('.')[0];
            var id = txt.GetValueOrDefault("id") ?? instance;
            devices.Add(new CastDevice("cast:" + id, name, CastKind.GoogleCast, host, srv?.Port ?? 8009, txt.GetValueOrDefault("md")));
        }
        return devices;
    }

    // ------------------------------------------------------------------ UPnP renderers (SSDP)

    private static async Task<IReadOnlyList<CastDevice>> FindUpnpAsync(HttpClient http, TimeSpan listen, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var search = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n");
        try { await udp.SendAsync(search, Ssdp, ct).ConfigureAwait(false); }
        catch (SocketException) { return []; }

        var locations = new HashSet<string>();
        await ReceiveAsync(udp, listen, ct, (data, _) =>
        {
            foreach (var line in Encoding.ASCII.GetString(data).Split("\r\n"))
                if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) locations.Add(line[9..].Trim());
        }).ConfigureAwait(false);

        var devices = await Task.WhenAll(locations.Select(l => DescribeAsync(http, l, ct))).ConfigureAwait(false);
        return devices.OfType<CastDevice>().ToList();
    }

    /// <summary>A renderer's name, model and control addresses, from its device description.</summary>
    public static async Task<CastDevice?> DescribeAsync(HttpClient http, string location, CancellationToken ct)
    {
        try
        {
            var baseUri = new Uri(location);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var xml = XDocument.Parse(await http.GetStringAsync(baseUri, timeout.Token).ConfigureAwait(false));
            XNamespace ns = "urn:schemas-upnp-org:device-1-0";
            // The device offering AVTransport (Sonos nests its renderer inside a ZonePlayer).
            var renderer = xml.Descendants(ns + "device").FirstOrDefault(d => Services(d).Any(s => Is(s, "AVTransport")));
            if (renderer is null) return null;
            var root = xml.Root?.Element(ns + "device") ?? renderer;
            Uri? Control(string service) => Services(renderer).FirstOrDefault(s => Is(s, service))?.Element(ns + "controlURL")?.Value is { } url
                ? new Uri(baseUri, url) : null;
            var name = root.Element(ns + "roomName")?.Value ?? root.Element(ns + "friendlyName")?.Value ?? baseUri.Host;
            var model = string.Join(' ', new[] { root.Element(ns + "manufacturer")?.Value, root.Element(ns + "modelName")?.Value }.Where(s => !string.IsNullOrEmpty(s)));
            var udn = root.Element(ns + "UDN")?.Value ?? location;
            return new CastDevice("upnp:" + udn, name, CastKind.Upnp, baseUri.Host, baseUri.Port, model, Control("AVTransport"), Control("RenderingControl"));

            IEnumerable<XElement> Services(XElement device) => device.Element(ns + "serviceList")?.Elements(ns + "service") ?? [];
            static bool Is(XElement service, string type) =>
                service.Element(XName.Get("serviceType", "urn:schemas-upnp-org:device-1-0"))?.Value.Contains(":service:" + type + ":", StringComparison.Ordinal) == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or UriFormatException)
        {
            return null;
        }
    }

    private static async Task ReceiveAsync(UdpClient udp, TimeSpan listen, CancellationToken ct, Action<byte[], IPEndPoint> handle)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(listen);
        while (!window.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                handle(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
        }
    }

    /// <summary>Just enough DNS to read mDNS answers: PTR, SRV, TXT and A records.</summary>
    internal static class Dns
    {
        public const ushort A = 1, Ptr = 12, Txt = 16, Srv = 33;

        public sealed record Record(string Name, ushort Type, string? Target = null, int Port = 0, string? Address = null,
            Dictionary<string, string>? Text = null, string? From = null);

        public static byte[] Query(string name, ushort type)
        {
            var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            bytes.AddRange(Name(name));
            bytes.AddRange([(byte)(type >> 8), (byte)type, 0, 1]);
            return [.. bytes];
        }

        private static IEnumerable<byte> Name(string name)
        {
            foreach (var label in name.Split('.'))
            {
                var b = Encoding.UTF8.GetBytes(label);
                yield return (byte)b.Length;
                foreach (var x in b) yield return x;
            }
            yield return 0;
        }

        public static IEnumerable<Record> Parse(byte[] data)
        {
            var records = new List<Record>();
            try
            {
                int qd = data[4] << 8 | data[5], count = (data[6] << 8 | data[7]) + (data[8] << 8 | data[9]) + (data[10] << 8 | data[11]);
                var off = 12;
                for (var i = 0; i < qd; i++) { ReadName(data, ref off); off += 4; }
                for (var i = 0; i < count; i++)
                {
                    var name = ReadName(data, ref off);
                    var type = (ushort)(data[off] << 8 | data[off + 1]);
                    var length = data[off + 8] << 8 | data[off + 9];
                    off += 10;
                    var start = off;
                    switch (type)
                    {
                        case Ptr:
                            records.Add(new Record(name, type, Target: ReadName(data, ref off)));
                            break;
                        case Srv:
                            var port = data[off + 4] << 8 | data[off + 5];
                            off += 6;
                            records.Add(new Record(name, type, Target: ReadName(data, ref off), Port: port));
                            break;
                        case A when length == 4:
                            records.Add(new Record(name, type, Address: new IPAddress(data.AsSpan(off, 4)).ToString()));
                            break;
                        case Txt:
                            var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            for (var p = off; p < start + length;)
                            {
                                var n = data[p];
                                var entry = Encoding.UTF8.GetString(data, p + 1, n);
                                var eq = entry.IndexOf('=');
                                if (eq > 0) text[entry[..eq]] = entry[(eq + 1)..];
                                p += 1 + n;
                            }
                            records.Add(new Record(name, type, Text: text));
                            break;
                    }
                    off = start + length;
                }
            }
            catch (IndexOutOfRangeException)
            {
                // A truncated or odd packet: keep what was read.
            }
            catch (ArgumentException)
            {
            }
            return records;
        }

        private static string ReadName(byte[] data, ref int off)
        {
            var labels = new List<string>();
            var end = -1;
            for (var guard = 0; guard < 128; guard++)
            {
                var n = data[off];
                if (n == 0) { off++; break; }
                if ((n & 0xC0) == 0xC0)
                {
                    if (end < 0) end = off + 2;
                    off = (n & 0x3F) << 8 | data[off + 1];
                    continue;
                }
                labels.Add(Encoding.UTF8.GetString(data, off + 1, n));
                off += 1 + n;
            }
            if (end >= 0) off = end;
            return string.Join('.', labels);
        }
    }
}

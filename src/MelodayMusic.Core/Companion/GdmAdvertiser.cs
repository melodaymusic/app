using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Companion;

public sealed class GdmAdvertiser : IDisposable
{
    public const string Capabilities = "timeline,playback,playqueues";

    private static readonly IPAddress Group = IPAddress.Parse("239.0.0.250");
    private const int SearchPort = 32412;
    private const int AnnouncePort = 32413;

    private readonly string _clientId;
    private readonly string _name;
    private readonly string _version;
    private readonly int _httpPort;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _udp;

    public GdmAdvertiser(string clientId, string name, string version, int httpPort, ILogger log)
    {
        _clientId = clientId;
        _name = name;
        _version = version;
        _httpPort = httpPort;
        _log = log;
    }

    public string Description => new StringBuilder()
        .Append("Content-Type: plex/media-player\r\n")
        .Append("Resource-Identifier: ").Append(_clientId).Append("\r\n")
        .Append("Name: ").Append(_name).Append("\r\n")
        .Append("Port: ").Append(_httpPort).Append("\r\n")
        .Append("Product: ").Append(Plex.PlexClientIdentity.ProductName).Append("\r\n")
        .Append("Version: ").Append(_version).Append("\r\n")
        .Append("Protocol: plex\r\n")
        .Append("Protocol-Version: 1\r\n")
        .Append("Protocol-Capabilities: ").Append(Capabilities).Append("\r\n")
        .Append("Device-Class: pc\r\n")
        .ToString();

    public void Start()
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, SearchPort));
        try
        {
            udp.JoinMulticastGroup(Group);
        }
        catch (SocketException ex)
        {
            _log.LogDebug("Couldn't join Plex discovery's multicast group: {Message}", ex.Message);
        }
        _udp = udp;
        _ = ListenAsync(udp, _stop.Token);
        Announce("HELLO");
    }

    private async Task ListenAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            var text = Encoding.UTF8.GetString(received.Buffer);
            if (!text.StartsWith("M-SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
            if (!CompanionServer.IsLocal(received.RemoteEndPoint.Address)) continue;
            var reply = Encoding.UTF8.GetBytes("HTTP/1.0 200 OK\r\n" + Description + "\r\n");
            try
            {
                await udp.SendAsync(reply, received.RemoteEndPoint, ct).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _log.LogDebug("Couldn't answer a Plex search: {Message}", ex.Message);
            }
        }
    }

    private void Announce(string verb)
    {
        try
        {
            var message = Encoding.UTF8.GetBytes(verb + " * HTTP/1.0\r\n" + Description + "\r\n");
            _udp?.Send(message, message.Length, new IPEndPoint(Group, AnnouncePort));
        }
        catch (SocketException ex)
        {
            _log.LogDebug("Couldn't announce to Plex apps: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        Announce("BYE");
        _stop.Cancel();
        _udp?.Dispose();
    }
}

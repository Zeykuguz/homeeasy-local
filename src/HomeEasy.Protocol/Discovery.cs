using System.Net;
using System.Net.Sockets;

namespace HomeEasy.Protocol;

public sealed record DiscoveredDevice(IPAddress Address, string Mac, byte Industry, byte Vendor, byte Type);

public static class Discovery
{
    public const int DevicePort = 12414;
    public const int LocalPort = 2415;
    private static readonly byte[] Request = Convert.FromHexString("AAAA0602FFFFFF0059");

    public static bool TryParseReply(ReadOnlySpan<byte> data, IPAddress from, out DiscoveredDevice? device)
    {
        device = null;
        if (data.Length < 13 || data[0] != 0xAA || data[1] != 0xAA || data[2] != 0x0C || data[3] != 0x03) return false;
        device = new DiscoveredDevice(from, Convert.ToHexString(data[7..13]).ToLowerInvariant(), data[4], data[5], data[6]);
        return true;
    }

    public static async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        TimeSpan timeout, IPAddress? target = null, CancellationToken ct = default)
    {
        target ??= IPAddress.Broadcast;
        var found = new Dictionary<IPAddress, DiscoveredDevice>();
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.EnableBroadcast = true;
        try { udp.Client.Bind(new IPEndPoint(IPAddress.Any, LocalPort)); }
        catch (SocketException) { udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var endpoint = new IPEndPoint(target, DevicePort);

        var sender = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await udp.SendAsync(Request, endpoint, cts.Token).ConfigureAwait(false);
                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            }
        }, cts.Token);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (TryParseReply(res.Buffer, res.RemoteEndPoint.Address, out var dev))
                    found.TryAdd(dev!.Address, dev);
            }
        }
        catch (OperationCanceledException) { }
        try { await sender.ConfigureAwait(false); } catch (OperationCanceledException) { }
        return found.Values.ToList();
    }
}

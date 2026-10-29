using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace JackOfAll.Lib.Services;

public sealed record LanDevice
{
    public string Ip { get; init; } = "";
    public string Name { get; init; } = "";
    public string Mac { get; init; } = "";
    public int PingMs { get; init; } = -1;
    public bool IsThisPc { get; init; }
    public bool IsRouter { get; init; }

    /// <summary>
    /// Locally administered bit set: phones and recent laptops use a random per-network MAC for
    /// privacy, so the MAC can't identify the maker.
    /// </summary>
    public bool PrivateMac => Mac.Length >= 2 && (Convert.ToByte(Mac[..2], 16) & 0x02) != 0;

    public uint SortKey => BitConverter.ToUInt32(IPAddress.Parse(Ip).GetAddressBytes().Reverse().ToArray());
}

public sealed record LanSubnet(IPAddress Local, IPAddress Network, int Prefix, string Gateway, string AdapterName)
{
    public string Display => $"{Network}/{Prefix} on {AdapterName}";
}

/// <summary>
/// Finds devices on the local network: pings every address in the subnet, then reads the ARP
/// table, which also catches devices that ignore ping but still answered the ARP request.
/// </summary>
public static class LanScanService
{
    private const int MaxHosts = 1024; // a /22; anything bigger takes too long to sweep politely

    public static LanSubnet? FindSubnet()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up)
                         .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
            {
                var ip = ni.GetIPProperties();
                var gw = ip.GatewayAddresses.Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gw is null) continue;
                var addr = ip.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (addr is null) continue;
                var prefix = Math.Max(addr.PrefixLength, 32 - (int)Math.Log2(MaxHosts));
                return new LanSubnet(addr.Address, Mask(addr.Address, prefix), prefix, gw.ToString(), ni.Name);
            }
        }
        catch (NetworkInformationException) { }
        return null;
    }

    public static async Task<List<LanDevice>> ScanAsync(LanSubnet subnet, IProgress<double>? progress, CancellationToken ct)
    {
        var baseIp = ToUInt(subnet.Network);
        var count = (1u << (32 - subnet.Prefix)) - 2; // skip network and broadcast addresses
        var replies = new Dictionary<string, int>();
        var done = 0;
        using var gate = new SemaphoreSlim(64);

        var tasks = Enumerable.Range(1, (int)count).Select(async i =>
        {
            var target = FromUInt(baseIp + (uint)i).ToString();
            await gate.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 700);
                if (reply.Status == IPStatus.Success)
                    lock (replies) replies[target] = (int)reply.RoundtripTime;
            }
            catch (PingException) { }
            finally
            {
                gate.Release();
                progress?.Report(Interlocked.Increment(ref done) / (double)count);
            }
        });
        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();

        var local = subnet.Local.ToString();
        var arp = ReadArpTable().Where(e => InSubnet(e.Ip, subnet)).ToDictionary(e => e.Ip.ToString(), e => e.Mac);
        var ips = replies.Keys.Union(arp.Keys).Append(local).Distinct().ToList();
        var localMac = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(subnet.Local)))
            ?.GetPhysicalAddress();

        var names = await Task.WhenAll(ips.Select(ip => ResolveNameAsync(ip, ct)));
        return ips.Select((ip, i) => new LanDevice
            {
                Ip = ip,
                Name = names[i],
                Mac = ip == local ? NetworkAdapterService.FormatMac(localMac) : arp.GetValueOrDefault(ip, ""),
                PingMs = ip == local ? 0 : replies.GetValueOrDefault(ip, -1),
                IsThisPc = ip == local,
                IsRouter = ip == subnet.Gateway,
            })
            .OrderBy(d => d.SortKey)
            .ToList();
    }

    private static async Task<string> ResolveNameAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(1500);
            var entry = await Dns.GetHostEntryAsync(ip, timeout.Token);
            return entry.HostName == ip ? "" : entry.HostName;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return "";
        }
    }

    private static bool InSubnet(IPAddress ip, LanSubnet s) => Mask(ip, s.Prefix).Equals(s.Network);

    private static IPAddress Mask(IPAddress ip, int prefix)
        => FromUInt(ToUInt(ip) & (prefix == 0 ? 0 : uint.MaxValue << (32 - prefix)));

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static IPAddress FromUInt(uint v) => new([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);

    private static List<(IPAddress Ip, string Mac)> ReadArpTable()
    {
        var result = new List<(IPAddress, string)>();
        var size = 0;
        GetIpNetTable(0, ref size, true);
        if (size <= 0) return result;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buf, ref size, true) != 0) return result;
            var n = Marshal.ReadInt32(buf);
            for (var i = 0; i < n; i++)
            {
                var row = buf + 4 + i * 24; // MIB_IPNETROW
                var macLen = Marshal.ReadInt32(row, 4);
                var type = Marshal.ReadInt32(row, 20);
                if (macLen != 6 || type == 2) continue; // 2 = invalid entry
                var mac = new byte[6];
                Marshal.Copy(row + 8, mac, 0, 6);
                if (mac.All(b => b == 0xff) || mac.All(b => b == 0) || (mac[0] & 0x01) != 0) continue; // broadcast / multicast
                var ip = new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 16)));
                result.Add((ip, string.Join(":", mac.Select(b => b.ToString("x2")))));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpNetTable(nint table, ref int size, bool order);
}

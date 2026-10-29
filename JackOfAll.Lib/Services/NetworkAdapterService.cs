using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JackOfAll.Lib.Services;

public sealed record NetworkAdapter
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "";
    public bool Up { get; init; }
    public bool IsDefaultRoute { get; init; }
    public long SpeedBps { get; init; }
    public string Mac { get; init; } = "";
    public IReadOnlyList<string> IPv4 { get; init; } = [];
    public IReadOnlyList<string> IPv6 { get; init; } = [];
    public IReadOnlyList<string> Gateways { get; init; } = [];
    public IReadOnlyList<string> Dns { get; init; } = [];
    public bool Dhcp { get; init; }
    public long BytesReceived { get; init; }
    public long BytesSent { get; init; }
}

public sealed record PublicIpInfo(string Ip, string Country, string Datacenter);

public static class NetworkAdapterService
{
    public static List<NetworkAdapter> Snapshot()
    {
        var result = new List<NetworkAdapter>();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var ni in all)
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel || IsPlumbing(ni)) continue;
            try
            {
                var ip = ni.GetIPProperties();
                var v4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => $"{a.Address}/{a.PrefixLength}").ToList();
                var v6 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .OrderBy(a => a.Address.IsIPv6LinkLocal)
                    .Select(a => a.Address.ToString()).ToList();
                var gateways = ip.GatewayAddresses.Select(g => g.Address)
                    .Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
                    .Select(a => a.ToString()).ToList();
                var stats = ni.GetIPStatistics();
                var dhcp = false;
                try { dhcp = ip.GetIPv4Properties()?.IsDhcpEnabled ?? false; }
                catch (NetworkInformationException) { }

                result.Add(new NetworkAdapter
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    Kind = KindName(ni),
                    Up = ni.OperationalStatus == OperationalStatus.Up,
                    IsDefaultRoute = ni.OperationalStatus == OperationalStatus.Up && gateways.Count > 0,
                    SpeedBps = ni.Speed,
                    Mac = FormatMac(ni.GetPhysicalAddress()),
                    IPv4 = v4,
                    IPv6 = v6,
                    Gateways = gateways,
                    Dns = ip.DnsAddresses.Select(a => a.ToString()).Distinct().ToList(),
                    Dhcp = dhcp,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent,
                });
            }
            catch (NetworkInformationException)
            {
                // an adapter can vanish between enumeration and query (VPN disconnect, USB unplug)
            }
        }
        return result
            .OrderByDescending(a => a.IsDefaultRoute)
            .ThenByDescending(a => a.Up)
            .ThenBy(a => a.Name)
            .ToList();
    }

    /// <summary>
    /// Windows lists every NDIS filter binding ("Wi-Fi-QoS Packet Scheduler-0000") and WAN Miniport
    /// as its own interface; they carry no addresses of their own and only bury the real adapters.
    /// </summary>
    private static bool IsPlumbing(NetworkInterface ni)
    {
        if (ni.Description.StartsWith("WAN Miniport", StringComparison.OrdinalIgnoreCase)) return true;
        if (ni.Name.Length > 5 && ni.Name[^5] == '-' && ni.Name[^4..].All(char.IsAsciiHexDigit)) return true;
        return ni.GetPhysicalAddress().GetAddressBytes().Length == 0 && ni.OperationalStatus != OperationalStatus.Up;
    }

    private static string KindName(NetworkInterface ni)
    {
        var d = ni.Description;
        if (d.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) return "Bluetooth";
        if (d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || d.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || d.Contains("VMware", StringComparison.OrdinalIgnoreCase) || d.Contains("WSL", StringComparison.OrdinalIgnoreCase)) return "Virtual";
        if (d.Contains("TAP", StringComparison.Ordinal) || d.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
            || d.Contains("VPN", StringComparison.OrdinalIgnoreCase)) return "VPN";
        return ni.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.Ppp => "PPP",
            NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile broadband",
            _ => ni.NetworkInterfaceType.ToString(),
        };
    }

    public static string FormatMac(PhysicalAddress? a)
    {
        var b = a?.GetAddressBytes() ?? [];
        return b.Length == 0 ? "" : string.Join(":", b.Select(x => x.ToString("x2")));
    }

    public static string FormatSpeed(long bps) => bps switch
    {
        <= 0 => "",
        >= 1_000_000_000 => $"{bps / 1e9:0.#} Gbps",
        >= 1_000_000 => $"{bps / 1e6:0} Mbps",
        _ => $"{bps / 1e3:0} Kbps",
    };

    public static string FormatRate(double bitsPerSecond) => bitsPerSecond switch
    {
        >= 1e9 => $"{bitsPerSecond / 1e9:0.00} Gbps",
        >= 1e6 => $"{bitsPerSecond / 1e6:0.0} Mbps",
        >= 1e3 => $"{bitsPerSecond / 1e3:0} Kbps",
        _ => $"{bitsPerSecond:0} bps",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>The address the internet sees, from Cloudflare's trace endpoint (plain key=value lines).</summary>
    public static async Task<PublicIpInfo> GetPublicIpAsync(CancellationToken ct)
    {
        var text = await Http.GetStringAsync("https://1.1.1.1/cdn-cgi/trace", ct);
        var map = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1].Trim());
        return new PublicIpInfo(map.GetValueOrDefault("ip", "?"), map.GetValueOrDefault("loc", ""), map.GetValueOrDefault("colo", ""));
    }
}

/// <summary>
/// Turns cumulative byte counters into live download/upload rates, summed over the adapters that
/// are up, and keeps two minutes of history for the chart.
/// </summary>
public sealed class ThroughputMeter
{
    public const int Capacity = 120;

    private readonly Dictionary<string, (long Rx, long Tx)> _last = [];
    private readonly Queue<(double Down, double Up)> _history = new();
    private long _lastTick;

    public double DownBps { get; private set; }
    public double UpBps { get; private set; }
    public IReadOnlyDictionary<string, (double Down, double Up)> PerAdapter { get; private set; } = new Dictionary<string, (double, double)>();

    public float[] DownHistory => _history.Select(h => (float)h.Down).ToArray();
    public float[] UpHistory => _history.Select(h => (float)h.Up).ToArray();

    public void Sample(IReadOnlyList<NetworkAdapter> adapters)
    {
        var now = Environment.TickCount64;
        var secs = _lastTick == 0 ? 0 : (now - _lastTick) / 1000.0;
        if (secs > 5) secs = 0; // sampling paused while hidden; start over rather than average across the gap
        _lastTick = now;

        double down = 0, up = 0;
        var per = new Dictionary<string, (double, double)>();
        foreach (var a in adapters)
        {
            if (_last.TryGetValue(a.Id, out var prev) && secs > 0.2 && a.Up)
            {
                // counters reset when an adapter reconnects; treat that sample as zero rather than negative
                var d = Math.Max(0, a.BytesReceived - prev.Rx) * 8 / secs;
                var u = Math.Max(0, a.BytesSent - prev.Tx) * 8 / secs;
                per[a.Id] = (d, u);
                if (a.Kind is not ("Virtual" or "VPN")) { down += d; up += u; } // a VPN's traffic is already counted on the real link
            }
            _last[a.Id] = (a.BytesReceived, a.BytesSent);
        }
        PerAdapter = per;
        if (secs <= 0.2) return;
        DownBps = down;
        UpBps = up;
        _history.Enqueue((down, up));
        while (_history.Count > Capacity) _history.Dequeue();
    }
}

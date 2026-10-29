using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace JackOfAll.Lib.Services;

public sealed record WifiLink
{
    public static readonly WifiLink Disconnected = new();

    public bool Connected { get; init; }
    public string Ssid { get; init; } = "";
    public string Profile { get; init; } = "";
    public string Bssid { get; init; } = "";
    public string Phy { get; init; } = "";
    public string Auth { get; init; } = "";
    public int Quality { get; init; }
    public int Rssi { get; init; } = -100;
    public int RxMbps { get; init; }
    public int TxMbps { get; init; }
    public int Channel { get; init; }
    public int FreqMHz { get; init; }
    public string Band => WifiBands.Name(FreqMHz);
}

public sealed record WifiBss
{
    public string Ssid { get; init; } = "";
    public string Bssid { get; init; } = "";
    public int Rssi { get; init; }
    public int FreqMHz { get; init; }
    public int Channel { get; init; }
    public int CenterMHz { get; init; }
    public int Width { get; init; } = 20;
    public bool Mine { get; init; }
    public bool Current { get; init; }
    public string Band => WifiBands.Name(FreqMHz);
    public string Shown => Ssid.Length == 0 ? "(hidden network)" : Ssid;

    /// <summary>Visual span in MHz: 2.4 GHz 20 MHz channels really bleed over 22 MHz.</summary>
    public double Span => FreqMHz < 3000 && Width == 20 ? 22 : Width;
}

public sealed class SavedWifiNetwork
{
    public string Profile { get; init; } = "";
    public string Ssid { get; set; } = "";
    public string Auth { get; set; } = "";
    public string QrType { get; set; } = "WPA";
    public bool Open { get; set; }
    public bool Enterprise { get; set; }
    public bool Revealed { get; set; }
    public bool Protected { get; set; }
    public string? Key { get; set; }
}

public static class WifiBands
{
    public static int Channel(int mhz) => mhz switch
    {
        2484 => 14,
        >= 2412 and < 2484 => (mhz - 2407) / 5,
        > 5925 and <= 7125 => (mhz - 5950) / 5,
        >= 5000 and <= 5925 => (mhz - 5000) / 5,
        _ => 0,
    };

    public static int Freq(int ch) => ch == 14 ? 2484 : ch <= 13 ? 2407 + 5 * ch : 5000 + 5 * ch;

    public static string Name(int mhz) => mhz == 0 ? "" : mhz < 3000 ? "2.4 GHz" : mhz <= 5925 ? "5 GHz" : "6 GHz";
}

/// <summary>
/// Thin wrapper over the native WLAN API (wlanapi.dll). Used instead of parsing netsh so it
/// doesn't depend on the Windows display language.
/// </summary>
public sealed class WlanClient : IDisposable
{
    private const int InterfaceInfoSize = 532, BssEntrySize = 360, ProfileInfoSize = 516;
    private const int OpCurrentConnection = 7, OpChannelNumber = 8, OpRssi = 0x10000102;
    private const int GetPlaintextKey = 4;

    private nint _handle;
    private Guid _iface;

    public bool Ok { get; }
    public string Error { get; } = "";
    public string Adapter { get; } = "";

    public WlanClient()
    {
        if (WlanOpenHandle(2, 0, out _, out _handle) != 0)
        {
            Error = "The Windows Wi-Fi service (WLAN AutoConfig) isn't running.";
            return;
        }
        if (WlanEnumInterfaces(_handle, 0, out var list) != 0)
        {
            Error = "Couldn't list Wi-Fi adapters.";
            return;
        }
        try
        {
            var n = Marshal.ReadInt32(list, 0);
            if (n == 0)
            {
                Error = "No Wi-Fi adapter found on this PC.";
                return;
            }
            var pick = 0;
            for (var i = 0; i < n; i++)
                if (Marshal.ReadInt32(list + 8 + i * InterfaceInfoSize, 528) == 1) { pick = i; break; } // 1 = connected
            var item = list + 8 + pick * InterfaceInfoSize;
            var guid = new byte[16];
            Marshal.Copy(item, guid, 0, 16);
            _iface = new Guid(guid);
            Adapter = Marshal.PtrToStringUni(item + 16) ?? "";
            Ok = true;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        WlanCloseHandle(_handle, 0);
        _handle = 0;
    }

    public WifiLink QueryLink()
    {
        if (!Ok || WlanQueryInterface(_handle, ref _iface, OpCurrentConnection, 0, out _, out var p, out _) != 0)
            return WifiLink.Disconnected;

        WifiLink link;
        try
        {
            link = new WifiLink
            {
                Connected = Marshal.ReadInt32(p, 0) == 1,
                Profile = Marshal.PtrToStringUni(p + 8) ?? "",
                Ssid = ReadSsid(p + 520),
                Bssid = ReadMac(p + 560),
                Phy = PhyName(Marshal.ReadInt32(p, 568)),
                Quality = Marshal.ReadInt32(p, 576),
                RxMbps = Marshal.ReadInt32(p, 580) / 1000,
                TxMbps = Marshal.ReadInt32(p, 584) / 1000,
                Auth = AuthName(Marshal.ReadInt32(p, 596)),
            };
        }
        finally
        {
            WlanFreeMemory(p);
        }

        if (QueryInt(OpChannelNumber, out var ch))
            link = link with { Channel = ch, FreqMHz = WifiBands.Freq(ch) };
        return link with { Rssi = QueryRssi(link.Quality) };
    }

    public int QueryRssi(int quality)
        => QueryInt(OpRssi, out var v) && v is < 0 and > -120 ? v : quality / 2 - 100;

    /// <summary>Asks the adapter to rescan. Results arrive a few seconds later via <see cref="GetNetworks"/>.</summary>
    public void Scan()
    {
        if (Ok) WlanScan(_handle, ref _iface, 0, 0, 0);
    }

    public List<WifiBss> GetNetworks()
    {
        var result = new List<WifiBss>();
        if (!Ok || WlanGetNetworkBssList(_handle, ref _iface, 0, 3, false, 0, out var list) != 0) return result;
        try
        {
            var n = Marshal.ReadInt32(list, 4);
            for (var i = 0; i < n; i++)
            {
                var e = list + 8 + i * BssEntrySize;
                var freq = Marshal.ReadInt32(e, 92) / 1000;
                var bss = new WifiBss
                {
                    Ssid = ReadSsid(e),
                    Bssid = ReadMac(e + 40),
                    Rssi = Marshal.ReadInt32(e, 56),
                    FreqMHz = freq,
                    Channel = WifiBands.Channel(freq),
                    CenterMHz = freq,
                };
                int ieOff = Marshal.ReadInt32(e, 352), ieSize = Marshal.ReadInt32(e, 356);
                if (ieSize is > 0 and < 65536)
                {
                    var ie = new byte[ieSize];
                    Marshal.Copy(e + ieOff, ie, 0, ieSize);
                    bss = WithWidth(bss, ie);
                }
                result.Add(bss);
            }
        }
        finally
        {
            WlanFreeMemory(list);
        }
        return result;
    }

    // Channel width comes from the HT (802.11n) and VHT (802.11ac) operation elements in the beacon.
    private static WifiBss WithWidth(WifiBss b, byte[] ie)
    {
        int htSec = 0, vhtWidth = -1, seg0 = 0, seg1 = 0;
        var htWide = false;
        for (var i = 0; i + 2 <= ie.Length;)
        {
            int id = ie[i], len = ie[i + 1], d = i + 2;
            if (d + len > ie.Length) break;
            if (id == 61 && len >= 2) { htSec = ie[d + 1] & 3; htWide = (ie[d + 1] & 4) != 0; }
            else if (id == 192 && len >= 3) { vhtWidth = ie[d]; seg0 = ie[d + 1]; seg1 = ie[d + 2]; }
            i = d + len;
        }
        if (htWide && htSec == 1) b = b with { Width = 40, CenterMHz = b.FreqMHz + 10 };
        else if (htWide && htSec == 3) b = b with { Width = 40, CenterMHz = b.FreqMHz - 10 };
        if (vhtWidth >= 1 && b.FreqMHz is > 5000 and <= 5925 && seg0 > 0)
        {
            b = seg1 != 0 && Math.Abs(seg1 - seg0) == 8
                ? b with { Width = 160, CenterMHz = 5000 + 5 * seg1 }
                : b with { Width = 80, CenterMHz = 5000 + 5 * seg0 };
        }
        return b;
    }

    public List<SavedWifiNetwork> GetSavedNetworks()
    {
        var result = new List<SavedWifiNetwork>();
        if (!Ok || WlanGetProfileList(_handle, ref _iface, 0, out var list) != 0) return result;
        var names = new List<string>();
        try
        {
            var n = Marshal.ReadInt32(list, 0);
            for (var i = 0; i < n; i++) names.Add(Marshal.PtrToStringUni(list + 8 + i * ProfileInfoSize) ?? "");
        }
        finally
        {
            WlanFreeMemory(list);
        }
        foreach (var name in names)
        {
            var saved = new SavedWifiNetwork { Profile = name, Ssid = name };
            Describe(saved, ProfileXml(name, plaintext: false));
            result.Add(saved);
        }
        return result;
    }

    /// <summary>Asks Windows for the saved key. Windows only decrypts it for administrators.</summary>
    public void Reveal(SavedWifiNetwork s)
    {
        var xml = ProfileXml(s.Profile, plaintext: true);
        s.Revealed = true;
        if (xml is null) { s.Protected = true; return; }
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            s.Protected = (Tag(doc, "protected") ?? "false") == "true";
            s.Key = s.Protected ? null : Tag(doc, "keyMaterial");
        }
        catch (XmlException)
        {
            s.Protected = true;
        }
    }

    private string? ProfileXml(string name, bool plaintext)
    {
        var flags = plaintext ? GetPlaintextKey : 0;
        if (WlanGetProfile(_handle, ref _iface, name, 0, out var xml, ref flags, out _) != 0) return null;
        var s = Marshal.PtrToStringUni(xml);
        WlanFreeMemory(xml);
        return s;
    }

    private static void Describe(SavedWifiNetwork s, string? xml)
    {
        if (xml is null) return;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var ssids = doc.GetElementsByTagName("SSID");
            if (ssids.Count > 0)
                foreach (XmlNode c in ssids[0]!.ChildNodes)
                    if (c.LocalName == "name") s.Ssid = c.InnerText;
            var auth = Tag(doc, "authentication") ?? "";
            s.Open = auth == "open" && (Tag(doc, "encryption") ?? "none") == "none";
            s.Enterprise = auth.Contains("WPA") && !auth.Contains("PSK") && !auth.Contains("SAE");
            s.Auth = auth switch
            {
                "open" => s.Open ? "Open" : "WEP",
                "shared" => "WEP",
                "WPA3SAE" => "WPA3-Personal",
                "OWE" => "Enhanced open",
                _ => auth.Replace("PSK", "-Personal"),
            };
            if (s.Enterprise) s.Auth += " (Enterprise)";
            s.QrType = s.Open ? "nopass" : s.Auth == "WEP" ? "WEP" : "WPA";
        }
        catch (XmlException)
        {
        }
    }

    private static string? Tag(XmlDocument d, string tag)
    {
        var l = d.GetElementsByTagName(tag);
        return l.Count > 0 ? l[0]!.InnerText : null;
    }

    private bool QueryInt(int op, out int v)
    {
        v = 0;
        if (!Ok || WlanQueryInterface(_handle, ref _iface, op, 0, out _, out var p, out _) != 0) return false;
        v = Marshal.ReadInt32(p);
        WlanFreeMemory(p);
        return true;
    }

    private static string ReadSsid(nint p)
    {
        var len = Math.Min(32, Marshal.ReadInt32(p, 0));
        var b = new byte[len];
        Marshal.Copy(p + 4, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    private static string ReadMac(nint p)
    {
        var b = new byte[6];
        Marshal.Copy(p, b, 0, 6);
        return string.Join(":", b.Select(x => x.ToString("x2")));
    }

    private static string PhyName(int t) => t switch
    {
        4 => "802.11a",
        5 => "802.11b",
        6 => "802.11g",
        7 => "Wi-Fi 4 (802.11n)",
        8 => "Wi-Fi 5 (802.11ac)",
        10 => "Wi-Fi 6 (802.11ax)",
        11 => "Wi-Fi 7 (802.11be)",
        _ => "802.11",
    };

    private static string AuthName(int a) => a switch
    {
        1 => "Open",
        2 => "WEP",
        3 => "WPA-Enterprise",
        4 => "WPA-Personal",
        6 => "WPA2-Enterprise",
        7 => "WPA2-Personal",
        8 or 11 => "WPA3-Enterprise",
        9 => "WPA3-Personal",
        10 => "Enhanced open",
        _ => "Secured",
    };

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint handle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(nint handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint p);
    [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(nint handle, ref Guid iface, int opCode, nint reserved, out int dataSize, out nint data, out int valueType);
    [DllImport("wlanapi.dll")] private static extern int WlanScan(nint handle, ref Guid iface, nint ssid, nint ieData, nint reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanGetNetworkBssList(nint handle, ref Guid iface, nint ssid, int bssType, bool securityEnabled, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern int WlanGetProfileList(nint handle, ref Guid iface, nint reserved, out nint list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern int WlanGetProfile(nint handle, ref Guid iface, string profileName, nint reserved, out nint xml, ref int flags, out int access);
}

/// <summary>
/// Polls the adapter on a background task: RSSI 4x a second, link details every second, the
/// neighbourhood every 4 s. Keeps the last minute of RSSI for the Hunt view.
/// </summary>
public sealed class WifiMonitor : IDisposable
{
    private const int HistoryCap = 240;

    private readonly object _lock = new();
    private readonly float[] _history = new float[HistoryCap];
    private readonly CancellationTokenSource _cts = new();
    private WifiLink _link = WifiLink.Disconnected;
    private IReadOnlyList<WifiBss> _networks = [];
    private int _historyCount, _historyHead;
    private long _samples;
    private volatile bool _scanRequested;

    public WlanClient Client { get; } = new();

    /// <summary>
    /// A scan takes the radio off-channel for a moment, which shows up as ping spikes, so periodic
    /// scans only run while the caller says the network list is on screen.
    /// </summary>
    public bool AutoScan { get; set; }

    public DateTime LastScan { get; private set; } = DateTime.MinValue;

    public WifiLink Link { get { lock (_lock) return _link; } }
    public IReadOnlyList<WifiBss> Networks { get { lock (_lock) return _networks; } }

    /// <summary>Total RSSI samples taken so far; used to read "samples since" a moment.</summary>
    public long SampleCount { get { lock (_lock) return _samples; } }

    public void Start() => Task.Run(() => LoopAsync(_cts.Token));

    public void ScanNow() => _scanRequested = true;

    public float[] History(int n)
    {
        lock (_lock)
        {
            var c = Math.Min(n, _historyCount);
            var r = new float[c];
            for (var i = 0; i < c; i++) r[i] = _history[(_historyHead - c + i + HistoryCap) % HistoryCap];
            return r;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Client.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        if (!Client.Ok) return;
        Client.Scan(); // one scan up front so the network list is complete the first time it's shown
        LastScan = DateTime.Now;
        int tick = 0, fetchAt = 14;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Poll(full: tick % 4 == 0);
                if (_scanRequested)
                {
                    _scanRequested = false;
                    Client.Scan();
                    LastScan = DateTime.Now;
                    fetchAt = tick + 14;
                }
                if (tick % 16 == 0 || tick == fetchAt) RefreshNetworks();
                if (AutoScan && tick % 80 == 79)
                {
                    Client.Scan();
                    LastScan = DateTime.Now;
                    fetchAt = tick + 14;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // a transient WLAN error (adapter switching off, driver reset) shouldn't end polling
            }
            tick++;
            try { await Task.Delay(250, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Poll(bool full)
    {
        WifiLink prev;
        lock (_lock) prev = _link;
        var link = full ? Client.QueryLink() : prev.Connected ? prev with { Rssi = Client.QueryRssi(prev.Quality) } : prev;
        lock (_lock)
        {
            var me = _networks.FirstOrDefault(b => b.Bssid == link.Bssid && b.FreqMHz > 0);
            if (me is not null) link = link with { FreqMHz = me.FreqMHz, Channel = me.Channel > 0 ? me.Channel : link.Channel };
            if (_link.Ssid != link.Ssid) { _historyCount = 0; _historyHead = 0; }
            _link = link;
            if (!link.Connected) return;
            _history[_historyHead] = link.Rssi;
            _historyHead = (_historyHead + 1) % HistoryCap;
            if (_historyCount < HistoryCap) _historyCount++;
            _samples++;
        }
    }

    private void RefreshNetworks()
    {
        var raw = Client.GetNetworks();
        lock (_lock)
        {
            var link = _link;
            _networks = raw
                .Select(b => b with
                {
                    Mine = link.Connected && b.Ssid == link.Ssid,
                    Current = link.Connected && b.Bssid == link.Bssid,
                })
                .OrderByDescending(b => b.Rssi)
                .ToList();
        }
    }
}

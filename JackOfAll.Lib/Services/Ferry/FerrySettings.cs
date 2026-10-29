using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace JackOfAll.Lib.Services.Ferry;

/// <summary>Ferry's own settings, kept in ferry.json next to the app's other data.</summary>
public sealed class FerrySettings
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "ferry.json");

    /// <summary>The secret in the phone link. Anyone on the network without it gets "link expired".</summary>
    public string Token { get; set; } = "";
    public int Port { get; set; } = 8765;
    public string Folder { get; set; } = "";
    public string Ip { get; set; } = "";
    public bool AutoCopy { get; set; } = true;

    public static FerrySettings Load()
    {
        FerrySettings? settings = null;
        try
        {
            if (File.Exists(FilePath)) settings = JsonSerializer.Deserialize<FerrySettings>(File.ReadAllText(FilePath));
        }
        catch { /* corrupt file: start over with a new link */ }
        settings ??= new FerrySettings();
        if (settings.Token.Length < 12) settings.Token = NewToken();
        if (settings.Folder.Length == 0) settings.Folder = Path.Combine(DownloadsFolder(), "Ferry");
        if (settings.Port is < 1024 or > 65000) settings.Port = 8765;
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureRoot();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);

    private static string DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, nint.Zero, out var pointer) == 0)
            {
                var path = Marshal.PtrToStringUni(pointer);
                Marshal.FreeCoTaskMem(pointer);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}

public sealed record FerryAddress(string Ip, string Label, int Rank)
{
    public override string ToString() => Label;
}

public static class FerryNetworks
{
    private static readonly string[] Virtual =
        ["virtual", "vethernet", "vmware", "virtualbox", "hyper-v", "wsl", "loopback", "vpn", "tap-", "wintun", "zerotier", "tailscale", "bluetooth"];

    /// <summary>IPv4 addresses a phone could reach, best first: real adapters with a gateway, Wi-Fi before Ethernet.</summary>
    public static List<FerryAddress> List()
    {
        var list = new List<FerryAddress>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = adapter.GetIPProperties();
                var gateway = props.GatewayAddresses.Any(static g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                var description = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
                var isVirtual = Virtual.Any(description.Contains);
                var wifi = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                foreach (var address in props.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = address.Address.ToString();
                    if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                    var rank = (gateway ? 0 : 20) + (isVirtual ? 40 : 0) + (wifi ? 0 : 5);
                    list.Add(new FerryAddress(ip, $"{ip}  ({(wifi ? "Wi-Fi" : adapter.Name)})", rank));
                }
            }
        }
        catch { }
        return [.. list.OrderBy(static a => a.Rank)];
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace JackOfAll.UserControls.Services.Undertow;

public sealed class NetConnection : INotifyPropertyChanged
{
    private string _host = "";

    public string Process { get; init; } = "";
    public int Pid { get; init; }
    public string Remote { get; init; } = "";
    public int RemotePort { get; init; }
    public int LocalPort { get; init; }
    public string State { get; init; } = "";
    public IPAddress? RemoteAddress { get; init; }

    public string Host
    {
        get => _host;
        set { _host = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Host))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>TCP connections with the process that owns each one, read with GetExtendedTcpTable (no netstat).</summary>
public static class NetworkConnections
{
    private static readonly ConcurrentDictionary<string, string> HostCache = new();

    private static readonly string[] States =
        ["", "Closed", "Listening", "SYN sent", "SYN received", "Established", "FIN wait 1", "FIN wait 2",
         "Close wait", "Closing", "Last ACK", "Time wait", "Deleting"];

    public static List<NetConnection> Get(bool includeLocal)
    {
        var names = new Dictionary<int, string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { names[p.Id] = p.ProcessName; } catch { }
            finally { p.Dispose(); }
        }

        var list = new List<NetConnection>();
        Read(AddressFamily.InterNetwork, list, names);
        Read(AddressFamily.InterNetworkV6, list, names);

        return list
            .Where(c => includeLocal || (c.State != "Listening" && c.RemoteAddress is not null
                                         && !IPAddress.IsLoopback(c.RemoteAddress)
                                         && !c.RemoteAddress.Equals(IPAddress.Any)
                                         && !c.RemoteAddress.Equals(IPAddress.IPv6Any)))
            .OrderBy(c => c.Process, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Remote)
            .ToList();
    }

    /// <summary>Fills in host names with reverse DNS in the background; results are cached for the session.</summary>
    public static void ResolveHosts(IEnumerable<NetConnection> connections, Action<Action> onUi)
    {
        foreach (var group in connections.Where(c => c.RemoteAddress is not null && c.State != "Listening")
                     .GroupBy(c => c.Remote))
        {
            if (HostCache.TryGetValue(group.Key, out var cached))
            {
                foreach (var c in group) c.Host = cached;
                continue;
            }
            var addr = group.First().RemoteAddress!;
            var items = group.ToList();
            _ = Task.Run(async () =>
            {
                string host;
                try
                {
                    var entry = await Dns.GetHostEntryAsync(addr).WaitAsync(TimeSpan.FromSeconds(4));
                    host = entry.HostName == addr.ToString() ? "" : entry.HostName;
                }
                catch { host = ""; }
                HostCache[group.Key] = host;
                onUi(() => { foreach (var c in items) c.Host = host; });
            });
        }
    }

    private static void Read(AddressFamily family, List<NetConnection> list, Dictionary<int, string> names)
    {
        var af = family == AddressFamily.InterNetwork ? 2 : 23;
        var size = 0;
        GetExtendedTcpTable(0, ref size, false, af, TcpTableOwnerPidAll, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, af, TcpTableOwnerPidAll, 0) != 0) return;
            var count = Marshal.ReadInt32(buf);
            var row = buf + 4;
            for (var i = 0; i < count; i++)
            {
                uint state, pid;
                int localPort, remotePort;
                IPAddress remote;
                if (af == 2)
                {
                    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
                    state = (uint)Marshal.ReadInt32(row);
                    localPort = Port(Marshal.ReadInt32(row, 8));
                    remote = new IPAddress((uint)Marshal.ReadInt32(row, 12));
                    remotePort = Port(Marshal.ReadInt32(row, 16));
                    pid = (uint)Marshal.ReadInt32(row, 20);
                    row += 24;
                }
                else
                {
                    // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid
                    localPort = Port(Marshal.ReadInt32(row, 20));
                    var bytes = new byte[16];
                    Marshal.Copy(row + 24, bytes, 0, 16);
                    remote = new IPAddress(bytes);
                    if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
                    remotePort = Port(Marshal.ReadInt32(row, 44));
                    state = (uint)Marshal.ReadInt32(row, 48);
                    pid = (uint)Marshal.ReadInt32(row, 52);
                    row += 56;
                }

                names.TryGetValue((int)pid, out var name);
                list.Add(new NetConnection
                {
                    Process = pid switch { 0 => "System Idle", 4 => "System", _ => name ?? $"PID {pid}" },
                    Pid = (int)pid,
                    RemoteAddress = remote,
                    Remote = remote.ToString(),
                    RemotePort = remotePort,
                    LocalPort = localPort,
                    State = state < States.Length ? States[state] : state.ToString()
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    private const int TcpTableOwnerPidAll = 5;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int af, int tableClass, uint reserved);
}

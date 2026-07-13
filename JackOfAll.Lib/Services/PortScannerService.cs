using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public sealed record PortResult(int Port, string Status, string Service, int Pid, string ProcessName);

public static partial class PortScannerService
{
    private static readonly int[] CommonPorts =
    [
        21, 22, 25, 53, 80, 443, 1433, 1521, 3000, 3306,
        3389, 4200, 5000, 5001, 5432, 5500, 5672, 6379,
        8000, 8080, 8081, 8443, 8888, 9090, 9200, 9300,
        15672, 27017, 44300
    ];

    public static async Task<List<PortResult>> ScanCommonPortsAsync(CancellationToken ct = default)
    {
        var portMap = await Task.Run(() => GetPortPidMap(), ct);
        var tasks = CommonPorts.Select(port => ScanPortAsync(port, portMap, ct));
        var scanned = await Task.WhenAll(tasks);
        var results = new List<PortResult>();
        results.AddRange(scanned.Where(r => r.Status == "Open"));
        results.AddRange(scanned.Where(r => r.Status != "Open"));
        return results;
    }

    public static async Task<List<PortResult>> ScanRangeAsync(int start, int end, CancellationToken ct = default)
    {
        var portMap = await Task.Run(() => GetPortPidMap(), ct);
        var ports = Enumerable.Range(start, end - start + 1);
        var tasks = ports.Select(port => ScanPortAsync(port, portMap, ct));
        var scanned = await Task.WhenAll(tasks);
        return scanned.Where(r => r.Status == "Open").ToList();
    }

    public static async Task<List<PortResult>> GetActiveTcpListenersAsync(CancellationToken ct = default)
    {
        var portMap = await Task.Run(() => GetPortPidMap(), ct);

        return await Task.Run(() =>
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = properties.GetActiveTcpListeners();
            return listeners
                .Where(ep => ep.Address.Equals(IPAddress.Any) ||
                             ep.Address.Equals(IPAddress.Loopback) ||
                             ep.Address.Equals(IPAddress.IPv6Any) ||
                             ep.Address.Equals(IPAddress.IPv6Loopback))
                .Select(ep =>
                {
                    portMap.TryGetValue(ep.Port, out var info);
                    return new PortResult(ep.Port, "Listening", GetServiceName(ep.Port), info.Pid, info.Name);
                })
                .DistinctBy(r => r.Port)
                .OrderBy(r => r.Port)
                .ToList();
        }, ct);
    }

    public static (bool Success, string Message) KillProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName;
            proc.Kill();
            proc.WaitForExit(3000);
            return (true, $"Killed {name} (PID {pid})");
        }
        catch (ArgumentException)
        {
            return (false, $"Process {pid} no longer exists.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to kill PID {pid}: {ex.Message}");
        }
    }

    private static async Task<PortResult> ScanPortAsync(int port, Dictionary<int, (int Pid, string Name)> portMap, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port, ct).AsTask();
            var completed = await Task.WhenAny(connectTask, Task.Delay(500, ct));
            if (completed == connectTask && connectTask.IsCompletedSuccessfully)
            {
                portMap.TryGetValue(port, out var info);
                return new PortResult(port, "Open", GetServiceName(port), info.Pid, info.Name);
            }
            return new PortResult(port, "Closed", "", 0, "");
        }
        catch
        {
            return new PortResult(port, "Closed", "", 0, "");
        }
    }

    private static Dictionary<int, (int Pid, string Name)> GetPortPidMap()
    {
        var map = new Dictionary<int, (int Pid, string Name)>();
        try
        {
            // Build PID -> name lookup in one shot
            var pidNames = new Dictionary<int, string>();
            foreach (var p in Process.GetProcesses())
            {
                try { pidNames[p.Id] = p.ProcessName; }
                catch { }
                finally { p.Dispose(); }
            }

            var psi = new ProcessStartInfo("netstat", "-ano -p TCP")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return map;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            foreach (Match match in NetstatRegex().Matches(output))
            {
                if (!int.TryParse(match.Groups["port"].Value, out var port)) continue;
                if (!int.TryParse(match.Groups["pid"].Value, out var pid)) continue;
                var state = match.Groups["state"].Value;
                if (state is not "LISTENING" and not "ESTABLISHED") continue;
                if (map.ContainsKey(port)) continue;

                pidNames.TryGetValue(pid, out var procName);
                map[port] = (pid, procName ?? "");
            }
        }
        catch
        {
            // netstat not available
        }

        return map;
    }

    [GeneratedRegex(@"TCP\s+[\d\.]+:(?<port>\d+)\s+[\d\.]+:\d+\s+(?<state>\w+)\s+(?<pid>\d+)")]
    private static partial Regex NetstatRegex();

    private static string GetServiceName(int port) => port switch
    {
        21 => "FTP",
        22 => "SSH",
        25 => "SMTP",
        53 => "DNS",
        80 => "HTTP",
        443 => "HTTPS",
        1433 => "SQL Server",
        1521 => "Oracle",
        3000 => "Dev Server",
        3306 => "MySQL",
        3389 => "RDP",
        4200 => "Angular",
        5000 or 5001 => "ASP.NET",
        5432 => "PostgreSQL",
        5500 => "Live Server",
        5672 => "RabbitMQ",
        6379 => "Redis",
        8000 => "Dev Server",
        8080 or 8081 => "HTTP Proxy",
        8443 => "HTTPS Alt",
        8888 => "Jupyter",
        9090 => "Prometheus",
        9200 or 9300 => "Elasticsearch",
        15672 => "RabbitMQ Mgmt",
        27017 => "MongoDB",
        44300 => "IIS Express",
        _ => ""
    };
}

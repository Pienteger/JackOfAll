using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JackOfAll.Lib.Services;

/// <summary>Ring buffer of round-trip times in ms; -1 marks a lost ping.</summary>
public sealed class PingSeries
{
    public const int Capacity = 240;

    private readonly object _lock = new();
    private readonly int[] _values = new int[Capacity];
    private int _count, _head;

    public string Target { get; internal set; } = "";

    public void Add(int ms)
    {
        lock (_lock)
        {
            _values[_head] = ms;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public void Clear()
    {
        lock (_lock) { _count = 0; _head = 0; }
    }

    public int[] Last(int n)
    {
        lock (_lock)
        {
            var c = Math.Min(n, _count);
            var r = new int[c];
            for (var i = 0; i < c; i++) r[i] = _values[(_head - c + i + Capacity) % Capacity];
            return r;
        }
    }
}

public readonly record struct PingStats(int Samples, int Lost, double AvgMs, double JitterMs)
{
    public double LossPercent => Samples > 0 ? 100.0 * Lost / Samples : 0;
    public bool AllLost => Samples > 0 && Lost == Samples;

    public static PingStats Of(IReadOnlyList<int> s)
    {
        double sum = 0, jitter = 0;
        int ok = 0, jitterN = 0, lost = 0, prev = -1;
        foreach (var x in s)
        {
            if (x < 0) { lost++; continue; }
            sum += x;
            ok++;
            if (prev >= 0) { jitter += Math.Abs(x - prev); jitterN++; }
            prev = x;
        }
        return new PingStats(s.Count, lost, ok > 0 ? sum / ok : 0, jitterN > 0 ? jitter / jitterN : 0);
    }
}

public enum PulseVerdictKind { NotConnected, UnderLoad, Listening, WifiProblem, InternetDown, IspProblem, AllClear }

public sealed record PulseVerdict(PulseVerdictKind Kind, string Headline, string Detail,
    bool Browsing = false, bool VideoCalls = false, bool Gaming = false, bool Streaming4K = false)
{
    public bool HasReadiness => Kind is PulseVerdictKind.WifiProblem or PulseVerdictKind.IspProblem or PulseVerdictKind.AllClear;
}

/// <summary>
/// Pings the router and the internet side by side every half second. Comparing the two hops is
/// what tells "your Wi-Fi is bad" apart from "your ISP is bad".
/// </summary>
public sealed class PingMonitor : IDisposable
{
    public const string InternetHost = "1.1.1.1";

    private readonly CancellationTokenSource _cts = new();

    public PingSeries Router { get; } = new();
    public PingSeries Internet { get; } = new() { Target = InternetHost };

    public void Start()
    {
        Task.Run(() => LoopAsync(Router, isGateway: true, _cts.Token));
        Task.Run(() => LoopAsync(Internet, isGateway: false, _cts.Token));
    }

    public void Dispose() => _cts.Cancel();

    private static async Task LoopAsync(PingSeries series, bool isGateway, CancellationToken ct)
    {
        var payload = new byte[32];
        using var ping = new Ping();
        for (var tick = 0; !ct.IsCancellationRequested; tick++)
        {
            var started = Environment.TickCount64;
            if (isGateway && tick % 20 == 0)
            {
                var gw = FindGateway();
                if (gw != series.Target) { series.Target = gw; series.Clear(); }
            }
            if (series.Target.Length > 0)
            {
                var ms = -1;
                try
                {
                    var reply = await ping.SendPingAsync(series.Target, 1000, payload);
                    if (reply.Status == IPStatus.Success) ms = (int)reply.RoundtripTime;
                }
                catch (PingException) { }
                catch (InvalidOperationException) { }
                series.Add(ms);
            }
            var wait = 500 - (int)(Environment.TickCount64 - started);
            try { if (wait > 0) await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The default gateway, preferring the Wi-Fi adapter's.</summary>
    public static string FindGateway()
    {
        try
        {
            var fallback = "";
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ga in ni.GetIPProperties().GatewayAddresses)
                {
                    if (ga.Address.AddressFamily != AddressFamily.InterNetwork || ga.Address.Equals(IPAddress.Any)) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return ga.Address.ToString();
                    if (fallback.Length == 0) fallback = ga.Address.ToString();
                }
            }
            return fallback;
        }
        catch (NetworkInformationException)
        {
            return "";
        }
    }

    public static PulseVerdict Judge(bool connected, bool speedTestRunning, PingStats router, PingStats internet, double? downloadMbps)
    {
        if (!connected)
            return new(PulseVerdictKind.NotConnected, "Not on Wi-Fi", "Connect to a network and the verdict will follow.");
        if (speedTestRunning)
            return new(PulseVerdictKind.UnderLoad, "Under load",
                "The speed test is filling the connection, so pings spike on purpose. Big spikes mean calls stutter whenever someone downloads.");
        if (internet.Samples < 8)
            return new(PulseVerdictKind.Listening, "Listening…", "Give it a few seconds to collect pings.");

        var routerSilent = router.Samples >= 10 && router.AllLost && internet.LossPercent < 50;
        var routerOk = routerSilent || (router.LossPercent < 3 && router.AvgMs < 25 && router.JitterMs < 15);
        var internetOk = internet.LossPercent < 3 && internet.AvgMs < 100 && internet.JitterMs < 25;

        var browsing = internet.LossPercent < 15;
        var calls = internet.LossPercent < 2 && internet.JitterMs < 30 && internet.AvgMs < 150;
        var gaming = internet.LossPercent < 1 && internet.JitterMs < 10 && internet.AvgMs < 60;
        var streaming = internet.LossPercent < 5 && (downloadMbps is null || downloadMbps >= 25);

        if (!routerOk)
        {
            var why = router.LossPercent >= 3 ? $"dropping {router.LossPercent:0}% of packets"
                : router.JitterMs >= 15 ? $"jittery ({router.JitterMs:0} ms)"
                : $"slow ({router.AvgMs:0} ms)";
            return new(PulseVerdictKind.WifiProblem, "It's your Wi-Fi.",
                $"The hop from this PC to your router is already {why}. Move closer, or try a quieter channel (see Airspace).",
                browsing, calls, gaming, streaming);
        }
        if (internet.LossPercent >= 95)
            return new(PulseVerdictKind.InternetDown, "Your internet is down.",
                "Wi-Fi to the router is fine, but nothing gets past it. Restart the modem or check with your ISP.");
        if (!internetOk)
            return new(PulseVerdictKind.IspProblem, "Wi-Fi is fine. Your ISP isn't.",
                "Your router answers quickly, so the slowdown is past it: the modem, the line, or your provider.",
                browsing, calls, gaming, streaming);
        return new(PulseVerdictKind.AllClear, "All clear.",
            "Quick, steady, no lost packets. Nothing to blame here."
            + (routerSilent ? " (Your router ignores pings, so this is judged on the internet side.)" : ""),
            browsing, calls, gaming, streaming);
    }
}

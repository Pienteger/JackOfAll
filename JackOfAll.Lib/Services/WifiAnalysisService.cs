namespace JackOfAll.Lib.Services;

public enum ChannelCrowd { Unknown, Empty, Quiet, Busy, Packed }

public sealed record ChannelScore(int Channel, double LoadDbm, bool IsCurrent);

public sealed record AirspaceReport
{
    public ChannelCrowd Crowd { get; init; }
    public IReadOnlyList<string> Advice { get; init; } = [];
    public IReadOnlyList<ChannelScore> Scoreboard { get; init; } = [];
    public int Count24 { get; init; }
    public int Count5 { get; init; }
    public int Count6 { get; init; }
}

public enum SignalLevel { Excellent, Great, Good, Fair, Weak, DeadZone }

public static class WifiAnalysisService
{
    public static SignalLevel Level(double dbm) => dbm switch
    {
        >= -50 => SignalLevel.Excellent,
        >= -60 => SignalLevel.Great,
        >= -67 => SignalLevel.Good,
        >= -75 => SignalLevel.Fair,
        >= -82 => SignalLevel.Weak,
        _ => SignalLevel.DeadZone,
    };

    public static (string Label, string Quip) Describe(SignalLevel level) => level switch
    {
        SignalLevel.Excellent => ("Excellent", "You could hug the router from here."),
        SignalLevel.Great => ("Great", "4K streaming territory."),
        SignalLevel.Good => ("Good", "Video calls will be smooth."),
        SignalLevel.Fair => ("Fair", "Fine for browsing. Calls may wobble."),
        SignalLevel.Weak => ("Weak", "Pages will crawl. Keep hunting."),
        _ => ("Dead zone", "Here be dragons. And no packets."),
    };

    /// <summary>
    /// Interference on a channel: the overlapping share of each foreign network's power, summed, in dBm.
    /// The caller's own SSID is excluded so a mesh or dual-band router doesn't count against itself.
    /// </summary>
    public static double ChannelLoad(IEnumerable<WifiBss> networks, int centerMhz, int width, string mySsid,
        out int overlapping, out WifiBss? loudest)
    {
        double mw = 0, lo = centerMhz - width / 2.0, hi = centerMhz + width / 2.0;
        overlapping = 0;
        loudest = null;
        foreach (var b in networks)
        {
            if (mySsid.Length > 0 && b.Ssid == mySsid) continue;
            var overlap = Math.Min(hi, b.CenterMHz + b.Span / 2) - Math.Max(lo, b.CenterMHz - b.Span / 2);
            if (overlap <= 0) continue;
            mw += overlap / width * Math.Pow(10, b.Rssi / 10.0);
            if (overlap / width > 0.1 && b.Rssi > -90)
            {
                overlapping++;
                if (loudest is null || b.Rssi > loudest.Rssi) loudest = b;
            }
        }
        return mw > 0 ? 10 * Math.Log10(mw) : -120;
    }

    public static ChannelCrowd CrowdOf(double loadDbm, int overlapping)
        => overlapping == 0 ? ChannelCrowd.Empty : loadDbm > -60 ? ChannelCrowd.Packed : loadDbm > -75 ? ChannelCrowd.Busy : ChannelCrowd.Quiet;

    public static AirspaceReport Analyze(WifiLink link, IReadOnlyList<WifiBss> networks, string gateway)
    {
        int n24 = networks.Count(b => b.FreqMHz < 3000);
        int n5 = networks.Count(b => b.FreqMHz is >= 3000 and <= 5925);
        int n6 = networks.Count(b => b.FreqMHz > 5925);
        if (!link.Connected)
        {
            return new AirspaceReport
            {
                Advice = ["Connect to a Wi-Fi network to see how crowded your channel is."],
                Count24 = n24, Count5 = n5, Count6 = n6,
            };
        }

        var me = networks.FirstOrDefault(b => b.Current);
        var freq = me?.FreqMHz ?? link.FreqMHz;
        var center = me?.CenterMHz ?? freq;
        var width = me?.Width ?? 20;
        var on24 = freq < 3000;
        var advice = new List<string>();

        var load = ChannelLoad(networks, center, on24 ? 20 : width, link.Ssid, out var count, out var loud);
        advice.Add($"You're on channel {link.Channel} ({WifiBands.Name(freq)}, {width} MHz wide).");
        if (freq > 5925)
            advice.Add("6 GHz is the quietest band there is. Nothing to fix here.");
        else if (count == 0)
            advice.Add("No other networks overlap your channel. Lovely.");
        else
            advice.Add($"{count} other network{(count == 1 ? " overlaps" : "s overlap")} it. The loudest is “{loud!.Shown}” at {loud.Rssi} dBm.");

        var scoreboard = new List<ChannelScore>();
        if (freq <= 5925)
        {
            int[] candidates = on24 ? [1, 6, 11] : [36, 40, 44, 48, 149, 153, 157, 161, 165];
            foreach (var ch in candidates)
                scoreboard.Add(new ChannelScore(ch, ChannelLoad(networks, WifiBands.Freq(ch), 20, link.Ssid, out _, out _), ch == link.Channel));
            var best = scoreboard.MinBy(s => s.LoadDbm)!;
            if (best.Channel != link.Channel && best.LoadDbm < load - 6)
                advice.Add($"Channel {best.Channel} is the quietest nearby. Set it in your router's settings"
                    + (gateway.Length > 0 ? $" (open http://{gateway} in a browser)." : "."));
            else if (count > 0)
                advice.Add("Your channel is already about as quiet as it gets around here.");
        }

        if (on24)
        {
            var alt = networks.Where(b => b.Mine && b.FreqMHz > 3000).MaxBy(b => b.Rssi);
            if (alt is not null && alt.Rssi > -75)
                advice.Add($"Your router also broadcasts on {alt.Band} here ({alt.Rssi} dBm). Connect to that for less interference and roughly 2-4x the speed.");
            else if (alt is not null)
                advice.Add($"Your router's {alt.Band} signal is weak here ({alt.Rssi} dBm), so 2.4 GHz is the right call in this spot.");
            else
                advice.Add($"Your router isn't visible on 5 GHz from here. If it's dual-band, turn 5 GHz on: {n24} network{(n24 == 1 ? " shares" : "s share")} 2.4 GHz around you, {n5} use{(n5 == 1 ? "s" : "")} 5 GHz.");
        }
        else
        {
            advice.Add("You're in the fast lane: 5/6 GHz has far less interference than 2.4 GHz.");
        }

        return new AirspaceReport
        {
            Crowd = CrowdOf(load, count),
            Advice = advice,
            Scoreboard = scoreboard,
            Count24 = n24, Count5 = n5, Count6 = n6,
        };
    }
}

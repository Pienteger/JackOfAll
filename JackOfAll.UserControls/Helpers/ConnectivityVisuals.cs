using System.Globalization;
using System.Windows;
using System.Windows.Media;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls.Helpers;

/// <summary>Theme-aware colours for the Connectivity tool, read at render time so light/dark switches apply at once.</summary>
internal static class NetPalette
{
    public static Color Good => ColorOf("HeaderColor", Color.FromRgb(0x34, 0xd3, 0x99));
    public static Color Okay => ColorOf("PayloadColor", Color.FromRgb(0xfb, 0xbf, 0x24));
    public static Color Bad => ColorOf("DangerBrush", Color.FromRgb(0xef, 0x44, 0x44));
    public static Color Accent => ColorOf("AccentBrush", Color.FromRgb(0x63, 0x66, 0xf1));
    public static Color Track => ColorOf("SurfaceBorder", Color.FromRgb(0x2d, 0x2e, 0x4a));
    public static Color Inset => ColorOf("AppBackground", Color.FromRgb(0x18, 0x19, 0x26));
    public static Color Text => ColorOf("TextPrimary", Colors.White);
    public static Color Secondary => ColorOf("TextSecondary", Colors.Gray);
    public static Color Dimmed => ColorOf("TextDimmed", Colors.DimGray);
    public static Color Router => Color.FromRgb(0x3b, 0x82, 0xf6);
    public static Color Internet => Color.FromRgb(0xa8, 0x55, 0xf7);

    private static Color ColorOf(string key, Color fallback)
        => Application.Current?.TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Red at -88 dBm, amber at -68, green from -48 up.</summary>
    public static Color Signal(double dbm)
    {
        var t = Math.Clamp((dbm + 88) / 40, 0, 1);
        return t < 0.5 ? Blend(Bad, Okay, t * 2) : Blend(Okay, Good, (t - 0.5) * 2);
    }

    public static Color LoadColor(double loadDbm) => loadDbm <= -75 ? Good : loadDbm <= -60 ? Okay : Bad;

    /// <summary>A stable colour per network name, so a router's radios share a colour.</summary>
    public static Color Hue(string key)
    {
        var h = 17;
        foreach (var c in key) h = unchecked(h * 31 + c);
        double hue = ((h & 0x7FFFFFFF) % 360) / 60.0, s = 0.6, v = 0.88;
        var i = (int)hue;
        double f = hue - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Compares the last second of signal (4 samples) with the few seconds before it, like a game
    /// of hot and cold. Used by the Wi-Fi hunt and the Bluetooth finder.
    /// </summary>
    public static (string Text, Color Color) Trend(float[] h)
    {
        const string steady = "Holding steady. Walk around and watch the needle.";
        var neutral = Secondary;
        if (h.Length < 12) return (steady, neutral);
        var diff = h[^4..].Average() - h[..^8].Average();
        return diff switch
        {
            >= 3 => ("▲  Warmer! Keep going this way.", Good),
            >= 1.5f => ("▲  A little warmer.", Blend(neutral, Good, 0.6)),
            <= -3 => ("▼  Colder. Turn back.", Bad),
            <= -1.5f => ("▼  A little colder.", Blend(neutral, Bad, 0.6)),
            _ => (steady, neutral),
        };
    }

    /// <summary>Gauge and bar position for a reading: -95 dBm empty, -30 dBm full.</summary>
    public static double Fraction(double dbm) => Math.Clamp((dbm + 95) / 65, 0, 1);
}

/// <summary>Base for the Connectivity drawn visuals: text helpers and a cheap "redraw now".</summary>
internal abstract class NetVisual : FrameworkElement
{
    protected FormattedText Text(string s, double size, Color c, bool bold = false)
        => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, NetPalette.Brush(c), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected void DrawCentered(DrawingContext dc, FormattedText t, Point center)
        => dc.DrawText(t, new Point(center.X - t.Width / 2, center.Y - t.Height / 2));

    public void Redraw() => InvalidateVisual();
}

/// <summary>The Hunt gauge: a 220° arc from -95 to -30 dBm with the best reading marked.</summary>
internal sealed class SignalGauge : NetVisual
{
    public double Dbm { get; set; } = double.NaN;
    public double Best { get; set; } = double.NaN;

    private const double StartDeg = 160, SweepDeg = 220;

    protected override void OnRender(DrawingContext dc)
    {
        double thick = 18, rad = Math.Min(ActualWidth / 2 - 40, ActualHeight * 0.62) - thick;
        if (rad < 30) return;
        var c = new Point(ActualWidth / 2, rad + thick + 30);
        DrawArc(dc, c, rad, StartDeg, SweepDeg, NetPalette.Track, thick);
        var has = !double.IsNaN(Dbm);
        if (has && NetPalette.Fraction(Dbm) > 0.01)
            DrawArc(dc, c, rad, StartDeg, SweepDeg * NetPalette.Fraction(Dbm), NetPalette.Signal(Dbm), thick);
        if (has && !double.IsNaN(Best))
        {
            var a = Rad(StartDeg + SweepDeg * NetPalette.Fraction(Best));
            var pen = new Pen(NetPalette.Brush(NetPalette.Text), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, At(c, rad - thick * 0.85, a), At(c, rad + thick * 0.85, a));
        }
        foreach (var tick in new[] { -90, -75, -60, -45, -30 })
            DrawCentered(dc, Text(tick.ToString(), 10, NetPalette.Dimmed), At(c, rad + 28, Rad(StartDeg + SweepDeg * NetPalette.Fraction(tick))));

        var num = Text(has ? Math.Round(Dbm).ToString(CultureInfo.CurrentCulture) : "--", 52, has ? NetPalette.Text : NetPalette.Dimmed, bold: true);
        dc.DrawText(num, new Point(c.X - num.Width / 2, c.Y - num.Height + 4));
        DrawCentered(dc, Text("dBm", 13, NetPalette.Secondary), new Point(c.X, c.Y + 14));
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Color color, double thick)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(c, r, Rad(startDeg)), false, false);
            g.ArcTo(At(c, r, Rad(startDeg + sweepDeg)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(NetPalette.Brush(color), thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
    private static Point At(Point c, double r, double a) => new(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
}

/// <summary>The last minute of RSSI as a filled line, coloured by the latest reading.</summary>
internal sealed class SignalSparkline : NetVisual
{
    public float[] Samples { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(NetPalette.Brush(NetPalette.Inset), null, r, 8, 8);
        var h = Samples;
        if (h.Length < 2) return;
        const double lo = -95, hi = -25, cap = 239;
        var pts = h.Select((v, i) => new Point(
            r.Right - 8 - (h.Length - 1 - i) * (r.Width - 16) / cap,
            r.Bottom - 6 - (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * (r.Height - 12))).ToArray();
        var color = NetPalette.Signal(h[^1]);

        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(pts[0].X, r.Bottom - 4), true, true);
            g.PolyLineTo(pts, true, true);
            g.LineTo(new Point(pts[^1].X, r.Bottom - 4), true, false);
        }
        var grad = new LinearGradientBrush(NetPalette.WithAlpha(color, 90), NetPalette.WithAlpha(color, 0), 90);
        dc.DrawGeometry(grad, null, fill);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false, false);
            g.PolyLineTo(pts[1..], true, true);
        }
        dc.DrawGeometry(null, new Pen(NetPalette.Brush(color), 2) { LineJoin = PenLineJoin.Round }, line);
    }
}

/// <summary>Channel map: each network drawn as a rounded hump across its real width, height = signal.</summary>
internal sealed class SpectrumChart : NetVisual
{
    public IReadOnlyList<WifiBss> Networks { get; set; } = [];
    public int FreqLo { get; set; }
    public int FreqHi { get; set; }
    public int[] GridChannels { get; set; } = [];
    public bool ShowDfs { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var p = new Rect(36, 8, Math.Max(10, ActualWidth - 44), Math.Max(10, ActualHeight - 30));
        double X(double f) => p.X + (f - FreqLo) / (FreqHi - FreqLo) * p.Width;
        double Y(double dbm) => p.Bottom - Math.Clamp((dbm + 95) / 75, 0, 1) * p.Height;
        var grid = new Pen(NetPalette.Brush(NetPalette.WithAlpha(NetPalette.Secondary, 50)), 1);

        foreach (var dbm in new[] { -30, -50, -70, -90 })
        {
            dc.DrawLine(grid, new Point(p.X, Y(dbm)), new Point(p.Right, Y(dbm)));
            var t = Text(dbm.ToString(), 9.5, NetPalette.Dimmed);
            dc.DrawText(t, new Point(p.X - t.Width - 6, Y(dbm) - t.Height / 2));
        }
        foreach (var ch in GridChannels)
        {
            var x = X(WifiBands.Freq(ch));
            dc.DrawLine(grid, new Point(x, p.Y), new Point(x, p.Bottom));
            DrawCentered(dc, Text(ch.ToString(), 9.5, NetPalette.Secondary), new Point(x, p.Bottom + 11));
        }
        if (ShowDfs)
        {
            var dfs = new Rect(X(5250), p.Y, X(5730) - X(5250), p.Height);
            dc.DrawRectangle(NetPalette.Brush(NetPalette.WithAlpha(NetPalette.Okay, 16)), null, dfs);
            DrawCentered(dc, Text("DFS (shared with radar)", 9.5, NetPalette.Dimmed), new Point(dfs.X + dfs.Width / 2, p.Y + 10));
        }
        if (Networks.Count == 0)
        {
            DrawCentered(dc, Text("Nothing here", 12, NetPalette.Dimmed), new Point(p.X + p.Width / 2, p.Y + p.Height / 2));
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(p.X, 0, p.Width, p.Bottom)));
        foreach (var b in Networks.OrderBy(b => b.Current).ThenBy(b => b.Rssi))
        {
            double x0 = X(b.CenterMHz - b.Span / 2), x1 = X(b.CenterMHz + b.Span / 2), top = Y(b.Rssi), bot = p.Bottom, e = (x1 - x0) * 0.16;
            var color = b.Mine ? NetPalette.Accent : NetPalette.Hue(b.Ssid);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x0, bot), true, true);
                g.BezierTo(new Point(x0 + e * 0.55, bot), new Point(x0 + e * 0.45, top), new Point(x0 + e, top), true, true);
                g.LineTo(new Point(x1 - e, top), true, true);
                g.BezierTo(new Point(x1 - e * 0.45, top), new Point(x1 - e * 0.55, bot), new Point(x1, bot), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(NetPalette.Brush(NetPalette.WithAlpha(color, (byte)(b.Current ? 90 : 42))),
                new Pen(NetPalette.Brush(NetPalette.WithAlpha(color, (byte)(b.Current ? 255 : 190))), b.Current ? 2.5 : 1.3), geo);
        }

        // label the strongest networks, skipping any label that would overlap one already placed
        var placed = new List<Rect>();
        foreach (var b in Networks.OrderByDescending(b => b.Current).ThenByDescending(b => b.Rssi))
        {
            if (b.Rssi < -86 && !b.Current) continue;
            var color = b.Current ? NetPalette.Text : b.Mine ? NetPalette.Accent : NetPalette.Hue(b.Ssid);
            var t = Text(b.Shown, 10, color, bold: b.Current);
            var x = Math.Clamp(X(b.CenterMHz) - t.Width / 2, p.X, Math.Max(p.X, p.Right - t.Width));
            var box = new Rect(x, Y(b.Rssi) - t.Height - 2, t.Width, t.Height);
            if (placed.Any(o => o.IntersectsWith(box))) continue;
            placed.Add(box);
            dc.DrawText(t, box.TopLeft);
        }
        dc.Pop();
    }
}

/// <summary>Router and internet round-trip times over the last two minutes; red ticks mark lost pings.</summary>
internal sealed class LatencyChart : NetVisual
{
    public int[] Router { get; set; } = [];
    public int[] Internet { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        var p = new Rect(48, 6, Math.Max(10, ActualWidth - 56), Math.Max(10, ActualHeight - 12));
        var ok = Router.Concat(Internet).Where(v => v >= 0).Order().ToArray();
        var p95 = ok.Length > 0 ? ok[Math.Min(ok.Length - 1, (int)(ok.Length * 0.95))] : 50;
        var max = Math.Max(40, (int)(Math.Ceiling(p95 * 1.25 / 20.0) * 20));
        var grid = new Pen(NetPalette.Brush(NetPalette.WithAlpha(NetPalette.Secondary, 50)), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = p.Bottom - p.Height * i / 4;
            dc.DrawLine(grid, new Point(p.X, y), new Point(p.Right, y));
            var t = Text($"{max * i / 4} ms", 9.5, NetPalette.Dimmed);
            dc.DrawText(t, new Point(p.X - t.Width - 6, y - t.Height / 2));
        }
        Plot(dc, p, Internet, max, NetPalette.Internet);
        Plot(dc, p, Router, max, NetPalette.Router);
    }

    private static void Plot(DrawingContext dc, Rect p, int[] s, int max, Color color)
    {
        if (s.Length == 0) return;
        var step = p.Width / (PingSeries.Capacity - 1);
        var pen = new Pen(NetPalette.Brush(color), 1.8) { LineJoin = PenLineJoin.Round };
        var lost = NetPalette.Brush(NetPalette.Bad);
        var run = new List<Point>();
        void Flush()
        {
            if (run.Count > 1)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(run[0], false, false);
                    g.PolyLineTo(run.Skip(1).ToList(), true, true);
                }
                dc.DrawGeometry(null, pen, geo);
            }
            run.Clear();
        }
        for (var i = 0; i < s.Length; i++)
        {
            var x = p.Right - (s.Length - 1 - i) * step;
            if (s[i] < 0)
            {
                Flush();
                dc.DrawRectangle(lost, null, new Rect(x - 1, p.Y, 2.5, 10));
                continue;
            }
            run.Add(new Point(x, p.Bottom - Math.Min(1, s[i] / (double)max) * p.Height));
        }
        Flush();
    }
}

/// <summary>Live download/upload throughput over the last two minutes, on a log-friendly auto scale.</summary>
internal sealed class RateChart : NetVisual
{
    public float[] Down { get; set; } = [];
    public float[] Up { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        var p = new Rect(64, 6, Math.Max(10, ActualWidth - 72), Math.Max(10, ActualHeight - 12));
        var peak = Down.Concat(Up).DefaultIfEmpty(0).Max();
        var max = Math.Max(1e6, NiceCeiling(peak * 1.15));
        var grid = new Pen(NetPalette.Brush(NetPalette.WithAlpha(NetPalette.Secondary, 50)), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = p.Bottom - p.Height * i / 4;
            dc.DrawLine(grid, new Point(p.X, y), new Point(p.Right, y));
            var t = Text(Lib.Services.NetworkAdapterService.FormatRate(max * i / 4), 9.5, NetPalette.Dimmed);
            dc.DrawText(t, new Point(p.X - t.Width - 6, y - t.Height / 2));
        }
        Plot(dc, p, Down, max, NetPalette.Good, fill: true);
        Plot(dc, p, Up, max, NetPalette.Router, fill: false);
    }

    /// <summary>Rounds up to 1, 2 or 5 x 10^n so gridlines land on readable numbers.</summary>
    private static double NiceCeiling(double v)
    {
        if (v <= 0) return 1;
        var e = Math.Pow(10, Math.Floor(Math.Log10(v)));
        var f = v / e;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * e;
    }

    private static void Plot(DrawingContext dc, Rect p, float[] s, double max, Color color, bool fill)
    {
        if (s.Length < 2) return;
        var step = p.Width / (Lib.Services.ThroughputMeter.Capacity - 1);
        var pts = s.Select((v, i) => new Point(p.Right - (s.Length - 1 - i) * step, p.Bottom - Math.Min(1, v / max) * p.Height)).ToArray();
        if (fill)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(pts[0].X, p.Bottom), true, true);
                g.PolyLineTo(pts, true, true);
                g.LineTo(new Point(pts[^1].X, p.Bottom), true, false);
            }
            dc.DrawGeometry(new LinearGradientBrush(NetPalette.WithAlpha(color, 80), NetPalette.WithAlpha(color, 0), 90), null, area);
        }
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false, false);
            g.PolyLineTo(pts[1..], true, true);
        }
        dc.DrawGeometry(null, new Pen(NetPalette.Brush(color), 1.8) { LineJoin = PenLineJoin.Round }, line);
    }
}

/// <summary>One column per candidate channel; shorter is quieter. The current channel is drawn solid.</summary>
internal sealed class ChannelScoreboard : NetVisual
{
    public IReadOnlyList<ChannelScore> Scores { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        if (Scores.Count == 0) return;
        const double gap = 8, labelH = 16;
        var colW = (ActualWidth - (Scores.Count - 1) * gap) / Scores.Count;
        var colH = ActualHeight - labelH;
        for (var i = 0; i < Scores.Count; i++)
        {
            var s = Scores[i];
            var col = new Rect(i * (colW + gap), 0, Math.Max(1, colW), Math.Max(1, colH));
            dc.DrawRoundedRectangle(NetPalette.Brush(NetPalette.Inset), null, col, 5, 5);
            var h = col.Height * Math.Clamp((s.LoadDbm + 100) / 60, 0.06, 1);
            var color = NetPalette.WithAlpha(NetPalette.LoadColor(s.LoadDbm), (byte)(s.IsCurrent ? 255 : 150));
            dc.DrawRoundedRectangle(NetPalette.Brush(color), null, new Rect(col.X, col.Bottom - h, col.Width, h), 5, 5);
            DrawCentered(dc, Text((s.IsCurrent ? "● " : "") + s.Channel, 10, s.IsCurrent ? NetPalette.Text : NetPalette.Secondary, s.IsCurrent),
                new Point(col.X + col.Width / 2, col.Bottom + labelH / 2 + 1));
        }
    }
}

/// <summary>A thin rounded meter for list rows.</summary>
public sealed class LevelBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(LevelBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, r = h / 2;
        dc.DrawRoundedRectangle(NetPalette.Brush(NetPalette.Track), null, new Rect(0, 0, ActualWidth, h), r, r);
        var w = ActualWidth * Math.Clamp(Fraction, 0, 1);
        if (w >= h) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, w, h), r, r);
    }
}

/// <summary>Renders QR modules dark-on-white with a quiet zone, whatever the app theme.</summary>
internal sealed class QrCodeImage : NetVisual
{
    public bool[,]? Modules { get; set; }

    // without this, anti-aliasing leaves faint seams between modules that can confuse phone cameras
    public QrCodeImage() => RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        var box = new Rect((ActualWidth - side) / 2, (ActualHeight - side) / 2, side, side);
        if (Modules is null)
        {
            dc.DrawRoundedRectangle(NetPalette.Brush(NetPalette.Inset), null, box, 12, 12);
            DrawCentered(dc, Text("QR code appears here", 12, NetPalette.Dimmed), new Point(box.X + side / 2, box.Y + side / 2));
            return;
        }
        dc.DrawRoundedRectangle(Brushes.White, null, box, 12, 12);
        int n = Modules.GetLength(0), quiet = 3;
        var cell = Math.Floor(side / (n + 2 * quiet));
        var off = new Point(box.X + (side - cell * n) / 2, box.Y + (side - cell * n) / 2);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    if (!Modules[y, x]) continue;
                    double px = off.X + x * cell, py = off.Y + y * cell;
                    g.BeginFigure(new Point(px, py), true, true);
                    g.PolyLineTo([new Point(px + cell, py), new Point(px + cell, py + cell), new Point(px, py + cell)], false, false);
                }
        geo.Freeze();
        dc.DrawGeometry(NetPalette.Brush(Color.FromRgb(14, 15, 32)), null, geo);
    }
}

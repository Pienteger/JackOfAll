using System.Windows;
using System.Windows.Media;
using JackOfAll.UserControls.Services.Hush;

namespace JackOfAll.UserControls.Helpers;

/// <summary>Soft ribbons, one per playing sound, whose height follows that sound's live level.</summary>
internal sealed class HushVisualizer : FrameworkElement
{
    private const int Points = 96;
    private float[] _disp = [];
    private IReadOnlyList<Sound> _sounds = [];

    public bool HasMotion { get; private set; }

    /// <summary>Eases the displayed levels toward the engine's measured ones; call about 30 times a second.</summary>
    public void Step(IReadOnlyList<Sound> sounds)
    {
        if (_disp.Length != sounds.Count) _disp = new float[sounds.Count];
        _sounds = sounds;
        var any = false;
        for (var i = 0; i < sounds.Count; i++)
        {
            _disp[i] += (sounds[i].Level - _disp[i]) * 0.12f;
            if (_disp[i] > 0.0005f || sounds[i].Level > 0) any = true;
        }
        if (any || HasMotion) InvalidateVisual();
        HasMotion = any;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (w < 10 || h < 10) return;

        var mid = h / 2;
        var lineBrush = TryFindResource("SurfaceBorder") as Brush ?? Brushes.Gray;
        dc.DrawLine(new Pen(lineBrush, 1), new Point(0, mid), new Point(w, mid));

        var t = Environment.TickCount64 / 1000.0;
        var order = Enumerable.Range(0, _disp.Length).Where(i => _disp[i] > 0.0015f).OrderBy(i => _disp[i]);
        foreach (var i in order)
        {
            var amp = h * 0.44 * Math.Min(1, Math.Sqrt(_disp[i] * 9));
            double k1 = 1.1 + i % 4 * 0.35, k2 = 0.8 + i % 3 * 0.45, sp1 = 0.5 + 0.13 * i, sp2 = 0.4 + 0.11 * i, ph = i * 1.3;
            var top = new Point[Points];
            var bottom = new Point[Points];
            for (var j = 0; j < Points; j++)
            {
                var u = j / (double)(Points - 1);
                var x = w * u;
                var taper = Math.Sin(Math.PI * u);
                var a = 0.55 + 0.45 * Math.Sin(u * k1 * 2 * Math.PI + t * sp1 + ph);
                var b = 0.55 + 0.45 * Math.Sin(u * k2 * 2 * Math.PI - t * sp2 + ph * 1.7);
                top[j] = new Point(x, mid - amp * taper * a);
                bottom[j] = new Point(x, mid + amp * taper * b * 0.8);
            }

            var c = _sounds[i].Color;
            var color = Color.FromRgb(c.R, c.G, c.B);
            var fill = new StreamGeometry();
            using (var g = fill.Open())
            {
                g.BeginFigure(top[0], true, true);
                g.PolyLineTo(top.Skip(1).ToList(), true, false);
                g.PolyLineTo(bottom.Reverse().ToList(), true, false);
            }
            fill.Freeze();
            var edge = new StreamGeometry();
            using (var g = edge.Open())
            {
                g.BeginFigure(top[0], false, false);
                g.PolyLineTo(top.Skip(1).ToList(), true, false);
            }
            edge.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(62, color.R, color.G, color.B)), null, fill);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B)), 1.6), edge);
        }
    }
}

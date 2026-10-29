using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JackOfAll.UserControls.Services.SpaceLens;
using DColor = System.Drawing.Color;
using DRect = System.Drawing.RectangleF;

namespace JackOfAll.UserControls.Helpers;

/// <summary>
/// Squarified treemap of a scanned folder. Tiles are painted straight into a bitmap, which stays fast with
/// tens of thousands of them; folder names and hover/selection outlines are drawn on top. Colors come from
/// the app theme, so it follows light and dark mode.
/// </summary>
internal sealed class TreemapCanvas : FrameworkElement
{
    private readonly record struct Item(Rect R, Node N); // device pixels

    private readonly List<Item> _items = [];
    private readonly Dictionary<Node, Rect> _rects = [];
    private WriteableBitmap? _bitmap;
    private DrawingGroup? _labels;
    private int[] _px = [];
    private int _w, _h;
    private double _scale = 1; // device pixels per DIP
    private bool _dirty = true;

    private Color _bg, _contrast;
    private Brush _headerText = Brushes.White;
    private bool _isDark = true;

    private static readonly Typeface HeaderFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface TileFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush TileText = Frozen(Color.FromRgb(22, 22, 26));
    private static readonly Brush JunkText = Frozen(Colors.White);
    private static readonly Color JunkColor = Color.FromRgb(214, 72, 64);

    public Node? Root { get; private set; }
    public Node? Hover { get; private set; }
    public Node? Selected { get; set; }
    public int HiCat { get; private set; } = -1;
    public string? HiExt { get; private set; }
    public HashSet<Node>? HiJunk { get; private set; }

    public event Action<Node?>? HoverChanged;
    public event Action<Node, MouseButtonEventArgs>? NodeClicked;
    public event Action<Node>? NodeDoubleClicked;
    public event Action<int, Node?>? Wheel;
    public event Action? BackRequested;

    public TreemapCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
    }

    public void SetRoot(Node? root)
    {
        Root = root;
        Hover = null;
        Rebuild();
    }

    public void SetHighlight(int cat, string? ext, HashSet<Node>? junk)
    {
        HiCat = cat;
        HiExt = ext;
        HiJunk = junk;
        Rebuild();
    }

    public void Rebuild()
    {
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Rebuild();
    }

    // ── painting ───────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // hit-testable everywhere
        if (Root is null || ActualWidth < 8 || ActualHeight < 8) return;
        if (_dirty) Paint();
        if (_bitmap is null) return;

        dc.DrawImage(_bitmap, new Rect(RenderSize));
        if (_labels is not null) dc.DrawDrawing(_labels);

        if (Selected is not null && FindRect(Selected, out var sr))
            Outline(dc, sr, Color.FromRgb(255, 200, 60), 2.5);
        if (Hover is not null && Hover != Selected && FindRect(Hover, out var hr))
            Outline(dc, hr, _isDark ? Colors.White : Color.FromRgb(20, 20, 30), 1.5);
    }

    private void Outline(DrawingContext dc, Rect px, Color c, double width)
    {
        var r = new Rect(px.X / _scale, px.Y / _scale, px.Width / _scale, px.Height / _scale);
        r.Inflate(-width / 2, -width / 2);
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(c), width), r);
    }

    private void ReadTheme()
    {
        _bg = (TryFindResource("SurfaceBackground") as SolidColorBrush)?.Color ?? Color.FromRgb(0x1f, 0x20, 0x37);
        _headerText = TryFindResource("TextPrimary") as Brush ?? Brushes.White;
        _isDark = 0.2126 * _bg.R + 0.7152 * _bg.G + 0.0722 * _bg.B < 128;
        _contrast = _isDark ? Colors.White : Colors.Black;
    }

    private void Paint()
    {
        _dirty = false;
        ReadTheme();
        _scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _w = Math.Max(1, (int)Math.Round(ActualWidth * _scale));
        _h = Math.Max(1, (int)Math.Round(ActualHeight * _scale));
        if (_bitmap is null || _bitmap.PixelWidth != _w || _bitmap.PixelHeight != _h)
        {
            _bitmap = new WriteableBitmap(_w, _h, 96 * _scale, 96 * _scale, PixelFormats.Pbgra32, null);
            _px = new int[_w * _h];
        }

        Array.Fill(_px, Pack(_bg));
        _items.Clear();
        _rects.Clear();

        var labels = new DrawingGroup();
        using (var ctx = labels.Open())
        {
            if (Root is not null)
            {
                var r = new DRect(0, 0, _w, _h);
                _rects[Root] = ToRect(r);
                var rootJunk = HiJunk?.Contains(Root) == true;
                if (Root.Children is not null)
                    Treemap.Squarify(Root.Children, r, (c, cr) => DrawNode(ctx, c, cr, 1, rootJunk));
            }
        }
        labels.Freeze();
        _labels = labels;
        _bitmap.WritePixels(new Int32Rect(0, 0, _w, _h), _px, _w * 4, 0);
    }

    private void DrawNode(DrawingContext ctx, Node n, DRect r, int depth, bool inJunk)
    {
        if (r.Width < 0.6f || r.Height < 0.6f) return;
        _items.Add(new Item(ToRect(r), n));
        _rects[n] = ToRect(r);
        var isJunkRoot = HiJunk?.Contains(n) == true;
        inJunk |= isJunkRoot;
        if (!n.IsDir)
        {
            DrawFile(ctx, n, r, inJunk);
            return;
        }

        var s = (float)_scale;
        var level = Math.Min(depth, 8);
        Fill(r, Blend(_bg, _contrast, 0.05 + level * 0.03));
        var hh = 17 * s;
        DRect inner;
        if (r.Width > 48 * s && r.Height > hh + 12 * s)
        {
            Fill(new DRect(r.X, r.Y, r.Width, hh), isJunkRoot ? JunkColor : Blend(_bg, _contrast, 0.14 + level * 0.03));
            Text(ctx, n.Name + "   " + Fmt.Size(n.Size), new DRect(r.X + 4 * s, r.Y, r.Width - 8 * s, hh), HeaderFace, 11,
                isJunkRoot ? JunkText : _headerText, true);
            inner = new DRect(r.X + 2, r.Y + hh, r.Width - 4, r.Height - hh - 2);
        }
        else
        {
            inner = new DRect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        }

        if (inner.Width >= 1.5f && inner.Height >= 1.5f && n.Children is not null)
            Treemap.Squarify(n.Children, inner, (c, cr) => DrawNode(ctx, c, cr, depth + 1, inJunk));
        if (isJunkRoot) Frame(r, JunkColor, Math.Max(2, (int)Math.Round(2 * s)));
    }

    private void DrawFile(DrawingContext ctx, Node n, DRect r, bool inJunk)
    {
        var match = HiJunk is not null ? inJunk
            : HiCat >= 0 ? n.Cat == HiCat
            : HiExt is null || string.Equals(n.Ext, HiExt);
        var dc = Cats.Colors[n.Cat];
        var c = match ? Color.FromRgb(dc.R, dc.G, dc.B) : Blend(_bg, _contrast, _isDark ? 0.16 : 0.12);
        if (r.Width < 4 || r.Height < 4)
        {
            Fill(r, c);
            return;
        }

        Cushion(r, c);
        var s = (float)_scale;
        if (r.Width > 64 * s && r.Height > 16 * s)
        {
            var text = r.Height > 32 * s ? n.Name + "\n" + Fmt.Size(n.Size) : n.Name;
            Text(ctx, text, new DRect(r.X + 4 * s, r.Y + 2 * s, r.Width - 8 * s, r.Height - 4 * s), TileFace, 10.5, TileText, false);
        }
    }

    private void Text(DrawingContext ctx, string text, DRect px, Typeface face, double size, Brush brush, bool singleLine)
    {
        var w = px.Width / _scale;
        var h = px.Height / _scale;
        if (w < 8 || h < 8) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, _scale)
        {
            MaxTextWidth = w,
            MaxTextHeight = h,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (singleLine) ft.MaxLineCount = 1;
        var y = px.Y / _scale + (singleLine ? Math.Max(0, (h - ft.Height) / 2) : 0);
        ctx.DrawText(ft, new Point(px.X / _scale, y));
    }

    // ── pixels ─────────────────────────────────────────────────────

    private static int Pack(Color c) => (255 << 24) | (c.R << 16) | (c.G << 8) | c.B;

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private bool ClipPx(DRect r, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = Math.Max(0, (int)Math.Round(r.X));
        y0 = Math.Max(0, (int)Math.Round(r.Y));
        x1 = Math.Min(_w, (int)Math.Round(r.Right));
        y1 = Math.Min(_h, (int)Math.Round(r.Bottom));
        return x1 > x0 && y1 > y0;
    }

    private void Fill(DRect r, Color c)
    {
        if (!ClipPx(r, out var x0, out var y0, out var x1, out var y1)) return;
        var v = Pack(c);
        for (var y = y0; y < y1; y++)
            Array.Fill(_px, v, y * _w + x0, x1 - x0);
    }

    private void Frame(DRect r, Color c, int t)
    {
        if (!ClipPx(r, out var x0, out var y0, out var x1, out var y1)) return;
        var v = Pack(c);
        for (var y = y0; y < y1; y++)
        {
            var row = y * _w;
            if (y < y0 + t || y >= y1 - t)
            {
                Array.Fill(_px, v, row + x0, x1 - x0);
                continue;
            }
            for (var x = x0; x < Math.Min(x1, x0 + t); x++) _px[row + x] = v;
            for (var x = Math.Max(x0, x1 - t); x < x1; x++) _px[row + x] = v;
        }
    }

    /// <summary>Light top-left to darker bottom-right, with a darker right and bottom edge, so tiles read as separate blocks.</summary>
    private void Cushion(DRect r, Color c)
    {
        if (!ClipPx(r, out var x0, out var y0, out var x1, out var y1)) return;
        var lr = c.R + (255 - c.R) * 0.30; var lg = c.G + (255 - c.G) * 0.30; var lb = c.B + (255 - c.B) * 0.30;
        var dr = c.R * 0.65; var dg = c.G * 0.65; var db = c.B * 0.65;
        double w = x1 - x0, h = y1 - y0;
        for (var y = y0; y < y1; y++)
        {
            var ty = (y - y0) / h;
            var row = y * _w;
            var lastRow = y == y1 - 1;
            for (var x = x0; x < x1; x++)
            {
                var t = ((x - x0) / w + ty) * 0.5;
                double rr = lr + (dr - lr) * t, gg = lg + (dg - lg) * t, bb = lb + (db - lb) * t;
                if (lastRow || x == x1 - 1) { rr *= 0.72; gg *= 0.72; bb *= 0.72; }
                _px[row + x] = (255 << 24) | ((int)rr << 16) | ((int)gg << 8) | (int)bb;
            }
        }
    }

    private static Rect ToRect(DRect r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));

    // ── hit testing and input ──────────────────────────────────────

    public Node? HitTest(Point dip)
    {
        if (Root is null || _dirty) return null;
        var p = new Point(dip.X * _scale, dip.Y * _scale);
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].R.Contains(p)) return _items[i].N;
        return null;
    }

    private bool FindRect(Node n, out Rect r)
    {
        for (var x = n; x is not null; x = x.Parent)
            if (x != Root && _rects.TryGetValue(x, out r)) return true;
        r = Rect.Empty;
        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var n = HitTest(e.GetPosition(this));
        if (n == Hover) return;
        Hover = n;
        InvalidateVisual();
        HoverChanged?.Invoke(n);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Hover is null) return;
        Hover = null;
        InvalidateVisual();
        HoverChanged?.Invoke(null);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ChangedButton == MouseButton.XButton1)
        {
            BackRequested?.Invoke();
            e.Handled = true;
            return;
        }

        var n = HitTest(e.GetPosition(this));
        if (n is null) return;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) NodeDoubleClicked?.Invoke(n);
        else NodeClicked?.Invoke(n, e);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Wheel?.Invoke(e.Delta, HitTest(e.GetPosition(this)));
        e.Handled = true;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

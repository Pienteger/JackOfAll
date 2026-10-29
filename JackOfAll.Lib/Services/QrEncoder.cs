using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JackOfAll.Lib.Services;

/// <summary>QR code encoder: byte mode, error correction level M, versions 1–10 (up to 213 bytes of UTF-8).</summary>
public static class QrEncoder
{
    private static readonly int[] EcLen = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private static readonly int[] Raw = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
    private static readonly int[][] Align =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34],
        [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    /// <summary>Renders text as a black-on-white QR image with the standard 4-module quiet zone, or null if it doesn't fit.</summary>
    public static BitmapSource? ToImage(string text, int pixelsPerModule = 8)
    {
        var modules = Encode(text);
        if (modules is null) return null;
        var n = modules.GetLength(0);
        var side = (n + 8) * pixelsPerModule;
        var pixels = new byte[side * side];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                if (!modules[y, x]) continue;
                for (var dy = 0; dy < pixelsPerModule; dy++)
                    Array.Fill(pixels, (byte)0, ((y + 4) * pixelsPerModule + dy) * side + (x + 4) * pixelsPerModule, pixelsPerModule);
            }
        var image = BitmapSource.Create(side, side, 96, 96, PixelFormats.Gray8, null, pixels, side);
        image.Freeze();
        return image;
    }

    /// <summary>Returns modules as [y, x], true = dark; null if the text doesn't fit in version 10.</summary>
    public static bool[,]? Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var ver = 0;
        for (var v = 1; v <= 10 && ver == 0; v++)
            if (4 + (v < 10 ? 8 : 16) + data.Length * 8 <= (Raw[v] - EcLen[v] * Blocks[v]) * 8) ver = v;
        if (ver == 0) return null;

        var dataCw = Raw[ver] - EcLen[ver] * Blocks[ver];
        var bits = new List<bool>();
        Put(bits, 4, 4);
        Put(bits, data.Length, ver < 10 ? 8 : 16);
        foreach (var b in data) Put(bits, b, 8);
        Put(bits, 0, Math.Min(4, dataCw * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var cw = new byte[dataCw];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));
        for (int i = bits.Count / 8, p = 0; i < dataCw; i++, p++) cw[i] = (byte)(p % 2 == 0 ? 0xEC : 0x11);

        int nb = Blocks[ver], ec = EcLen[ver], shortLen = Raw[ver] / nb, numShort = nb - Raw[ver] % nb;
        var div = RsDivisor(ec);
        var dBlocks = new List<byte[]>();
        var eBlocks = new List<byte[]>();
        for (int i = 0, k = 0; i < nb; i++)
        {
            var len = shortLen - ec + (i < numShort ? 0 : 1);
            var d = cw[k..(k + len)];
            k += len;
            dBlocks.Add(d);
            eBlocks.Add(RsRemainder(d, div));
        }
        var all = new List<byte>();
        for (var i = 0; i < dBlocks[nb - 1].Length; i++) foreach (var d in dBlocks) if (i < d.Length) all.Add(d[i]);
        for (var i = 0; i < ec; i++) foreach (var e in eBlocks) all.Add(e[i]);

        var size = 17 + 4 * ver;
        bool[,] m = new bool[size, size], fn = new bool[size, size];
        DrawFunction(m, fn, ver, size);
        var bit = 0;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (!fn[y, x] && bit < all.Count * 8) { m[y, x] = ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0; bit++; }
                }
        }

        int best = 0, bestScore = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(m, fn, mask, size);
            DrawFormat(m, fn, mask, size);
            var score = Penalty(m, size);
            if (score < bestScore) { bestScore = score; best = mask; }
            ApplyMask(m, fn, mask, size); // XOR undoes it
        }
        ApplyMask(m, fn, best, size);
        DrawFormat(m, fn, best, size);
        return m;
    }

    private static void Put(List<bool> bits, int val, int len) { for (var i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }

    private static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; }

    private static void DrawFunction(bool[,] m, bool[,] fn, int ver, int size)
    {
        for (var i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
        int[][] finders = [[3, 3], [size - 4, 3], [3, size - 4]];
        foreach (var f in finders)
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = f[0] + dx, yy = f[1] + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size) Set(m, fn, xx, yy, dist != 2 && dist != 4);
                }
        var al = Align[ver];
        for (var i = 0; i < al.Length; i++)
            for (var j = 0; j < al.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == al.Length - 1) || (i == al.Length - 1 && j == 0)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++) Set(m, fn, al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormat(m, fn, 0, size); // reserves the area; redrawn per mask
        if (ver < 7) return;
        var rem = ver;
        for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var vbits = ver << 12 | rem;
        for (var i = 0; i < 18; i++)
        {
            var b = ((vbits >> i) & 1) != 0;
            int a = size - 11 + i % 3, c = i / 3;
            Set(m, fn, a, c, b);
            Set(m, fn, c, a, b);
        }
    }

    private static void DrawFormat(bool[,] m, bool[,] fn, int mask, int size)
    {
        int data = 0 << 3 | mask, rem = data; // error correction level M = 00
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var b = (data << 10 | rem) ^ 0x5412;
        bool Bit(int i) => ((b >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++) Set(m, fn, 8, i, Bit(i));
        Set(m, fn, 8, 7, Bit(6));
        Set(m, fn, 8, 8, Bit(7));
        Set(m, fn, 7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, Bit(i));
        for (var i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, Bit(i));
        Set(m, fn, 8, size - 8, true);
    }

    private static void ApplyMask(bool[,] m, bool[,] fn, int mask, int size)
    {
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (fn[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    // Runs, 2x2 blocks, finder look-alikes and dark balance, as in the spec's mask evaluation.
    private static int Penalty(bool[,] m, int size)
    {
        int score = 0, dark = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < size; a++)
            {
                int run = 0, window = 0;
                var prev = false;
                for (var b = 0; b < size; b++)
                {
                    var v = pass == 0 ? m[a, b] : m[b, a];
                    if (b > 0 && v == prev) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                    else run = 1;
                    prev = v;
                    window = ((window << 1) | (v ? 1 : 0)) & 0x7FF;
                    if (b >= 10 && (window == 0x5D0 || window == 0x05D)) score += 40; // 10111010000 / 00001011101
                }
            }
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (m[y, x]) dark++;
                if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) score += 3;
            }
        int total = size * size, k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return score + Math.Max(0, k) * 10;
    }

    private static byte[] RsDivisor(int degree)
    {
        var r = new byte[degree];
        r[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < r.Length; j++)
            {
                r[j] = (byte)Mul(r[j], root);
                if (j + 1 < r.Length) r[j] ^= r[j + 1];
            }
            root = Mul(root, 2);
        }
        return r;
    }

    private static byte[] RsRemainder(byte[] data, byte[] div)
    {
        var r = new byte[div.Length];
        foreach (var b in data)
        {
            var factor = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1);
            r[^1] = 0;
            for (var i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
        }
        return r;
    }

    private static int Mul(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }
}

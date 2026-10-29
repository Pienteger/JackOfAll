using System.Text;

namespace JackOfAll.Lib.Services;

/// <summary>QR code encoder: byte mode, error correction level M, versions 1-10 (up to ~200 bytes).</summary>
public static class QrCodeService
{
    private static readonly int[] EcLen = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private static readonly int[] RawCodewords = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
    private static readonly int[][] AlignmentCenters =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34],
        [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    /// <summary>The "WIFI:" payload phone cameras understand for joining a network.</summary>
    public static string WifiPayload(string ssid, string authType, string? password)
    {
        var pw = authType == "nopass" ? "" : $"P:{Escape(password ?? "")};";
        return $"WIFI:T:{authType};S:{Escape(ssid)};{pw};";
    }

    private static string Escape(string v)
    {
        var b = new StringBuilder();
        foreach (var c in v)
        {
            if ("\\;,:\"".Contains(c)) b.Append('\\');
            b.Append(c);
        }
        return b.ToString();
    }

    /// <summary>Returns modules as [y, x] (true = dark), or null if the text doesn't fit in version 10.</summary>
    public static bool[,]? Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        var ver = 0;
        for (var v = 1; v <= 10 && ver == 0; v++)
            if (4 + (v < 10 ? 8 : 16) + data.Length * 8 <= (RawCodewords[v] - EcLen[v] * Blocks[v]) * 8) ver = v;
        if (ver == 0) return null;

        var dataCw = RawCodewords[ver] - EcLen[ver] * Blocks[ver];
        var bits = new List<bool>();
        Put(bits, 4, 4);
        Put(bits, data.Length, ver < 10 ? 8 : 16);
        foreach (var b in data) Put(bits, b, 8);
        Put(bits, 0, Math.Min(4, dataCw * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var cw = new byte[dataCw];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));
        for (int i = bits.Count / 8, p = 0; i < dataCw; i++, p++) cw[i] = (byte)(p % 2 == 0 ? 0xEC : 0x11);

        int nb = Blocks[ver], ec = EcLen[ver], shortLen = RawCodewords[ver] / nb, numShort = nb - RawCodewords[ver] % nb;
        var divisor = RsDivisor(ec);
        var dataBlocks = new List<byte[]>();
        var ecBlocks = new List<byte[]>();
        for (int i = 0, k = 0; i < nb; i++)
        {
            var len = shortLen - ec + (i < numShort ? 0 : 1);
            var d = cw.AsSpan(k, len).ToArray();
            k += len;
            dataBlocks.Add(d);
            ecBlocks.Add(RsRemainder(d, divisor));
        }
        var all = new List<byte>();
        for (var i = 0; i < dataBlocks[^1].Length; i++)
            foreach (var d in dataBlocks) if (i < d.Length) all.Add(d[i]);
        for (var i = 0; i < ec; i++)
            foreach (var e in ecBlocks) all.Add(e[i]);

        var size = 17 + 4 * ver;
        var m = new bool[size, size];
        var fn = new bool[size, size];
        DrawFunctionPatterns(m, fn, ver, size);

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
                    if (fn[y, x] || bit >= all.Count * 8) continue;
                    m[y, x] = ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                    bit++;
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

    private static void Put(List<bool> bits, int val, int len)
    {
        for (var i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0);
    }

    private static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark)
    {
        m[y, x] = dark;
        fn[y, x] = true;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int ver, int size)
    {
        for (var i = 0; i < size; i++)
        {
            Set(m, fn, 6, i, i % 2 == 0);
            Set(m, fn, i, 6, i % 2 == 0);
        }
        foreach (var (fx, fy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = fx + dx, yy = fy + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size) Set(m, fn, xx, yy, dist != 2 && dist != 4);
                }
        var al = AlignmentCenters[ver];
        for (var i = 0; i < al.Length; i++)
            for (var j = 0; j < al.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == al.Length - 1) || (i == al.Length - 1 && j == 0)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Set(m, fn, al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
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
        int data = mask, rem = data; // error correction level M = 00 in the top two bits
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
                    if (b > 0 && v == prev)
                    {
                        run++;
                        if (run == 5) score += 3;
                        else if (run > 5) score++;
                    }
                    else run = 1;
                    prev = v;
                    window = ((window << 1) | (v ? 1 : 0)) & 0x7FF;
                    if (b >= 10 && window is 0x5D0 or 0x05D) score += 40; // 10111010000 / 00001011101
                }
            }
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (m[y, x]) dark++;
                if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                    score += 3;
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
                r[j] = (byte)GfMul(r[j], root);
                if (j + 1 < r.Length) r[j] ^= r[j + 1];
            }
            root = GfMul(root, 2);
        }
        return r;
    }

    private static byte[] RsRemainder(byte[] data, byte[] divisor)
    {
        var r = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1);
            r[^1] = 0;
            for (var i = 0; i < r.Length; i++) r[i] ^= (byte)GfMul(divisor[i], factor);
        }
        return r;
    }

    private static int GfMul(int x, int y)
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

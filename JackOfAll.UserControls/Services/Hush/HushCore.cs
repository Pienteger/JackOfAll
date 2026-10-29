// Copied from the standalone Hush app (Hush.Core.cs). Keep the two in sync; it is C# 5 so it still builds there.
#nullable disable
// Hush core: DSP building blocks, the ten synthesized sounds, mix presets and the waveOut audio engine.
// No UI code here. Written in C# 5 so it compiles with the .NET Framework compiler that ships with Windows
// (standalone Hush) and with modern .NET (the JackOfAll copy).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace JackOfAll.UserControls.Services.Hush
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr hwo, int device, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr hwo);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    // ------------------------------------------------------------------ DSP building blocks

    static class Dsp
    {
        public const float Fs = 44100f;
        public const float TwoPi = 6.28318530718f;
        public static float Smooth(float x) { return x * x * (3 - 2 * x); }
    }

    sealed class Rng
    {
        uint s;
        public Rng(uint seed) { s = seed * 2654435761u + 0x9E3779B9u; if (s == 0) s = 1; }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float W() { return (int)Next() * 4.6566129e-10f; }          // white, -1..1
        public float U() { return (Next() >> 8) * 5.9604645e-8f; }         // uniform, 0..1
        public float Range(float a, float b) { return a + (b - a) * U(); }
    }

    sealed class Pink // Paul Kellet's refined pink noise filter
    {
        float b0, b1, b2, b3, b4, b5, b6;
        public float Next(float w)
        {
            b0 = 0.99886f * b0 + w * 0.0555179f; b1 = 0.99332f * b1 + w * 0.0750759f;
            b2 = 0.96900f * b2 + w * 0.1538520f; b3 = 0.86650f * b3 + w * 0.3104856f;
            b4 = 0.55000f * b4 + w * 0.5329522f; b5 = -0.7616f * b5 - w * 0.0168980f;
            float o = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
            b6 = w * 0.115926f;
            return o * 0.11f;
        }
    }

    sealed class Brown
    {
        float b;
        public float Next(float w) { b = (b + 0.02f * w) / 1.02f; return b * 3.5f; }
    }

    sealed class Biquad // RBJ cookbook, transposed direct form II
    {
        public const int LP = 0, HP = 1, BP = 2;
        float b0, b1, b2, a1, a2, z1, z2;
        public Biquad(int type, float f, float q) { Set(type, f, q); }
        public void Set(int type, float f, float q)
        {
            if (f > 20000) f = 20000; if (f < 10) f = 10;
            double w0 = 2 * Math.PI * f / Dsp.Fs, c = Math.Cos(w0), al = Math.Sin(w0) / (2 * q), a0 = 1 + al, x0, x1, x2;
            if (type == LP) { x0 = (1 - c) / 2; x1 = 1 - c; x2 = x0; }
            else if (type == HP) { x0 = (1 + c) / 2; x1 = -(1 + c); x2 = x0; }
            else { x0 = al; x1 = 0; x2 = -al; }
            b0 = (float)(x0 / a0); b1 = (float)(x1 / a0); b2 = (float)(x2 / a0);
            a1 = (float)(-2 * c / a0); a2 = (float)((1 - al) / a0);
        }
        public float P(float x)
        {
            x += 1e-18f; // keeps the state out of denormal territory when the input goes silent
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    sealed class Drift // a value that wanders smoothly between random targets; Step() once per 64 samples
    {
        public float V;
        float target, coef;
        readonly float min, max, minT, maxT;
        int left;
        readonly Rng rng;
        public Drift(Rng r, float min, float max, float minT, float maxT, float tau)
        {
            rng = r; this.min = min; this.max = max; this.minT = minT; this.maxT = maxT;
            V = target = (min + max) * 0.5f;
            coef = 1f - (float)Math.Exp(-64.0 / (tau * Dsp.Fs));
        }
        public void Step()
        {
            left -= 64;
            if (left <= 0) { target = rng.Range(min, max); left = (int)(rng.Range(minT, maxT) * Dsp.Fs); }
            V += (target - V) * coef;
        }
    }

    // ------------------------------------------------------------------ the sounds

    abstract class Sound
    {
        public readonly string Name, Glyph;
        public readonly Color Color;
        public volatile bool On;
        public float Vol = 0.5f, Calib = 1f;
        public float Cur, Level; // owned by the audio thread
        protected readonly Rng rng;
        protected Sound(string name, string glyph, Color color, uint seed) { Name = name; Glyph = glyph; Color = color; rng = new Rng(seed); }
        public abstract void Render(float[] l, float[] r, int n); // adds n samples into l/r

        public static List<Sound> CreateAll()
        {
            List<Sound> list = new List<Sound>
            {
                new Rain(), new Thunder(), new Wind(), new Ocean(), new Brook(),
                new Fire(), new Night(), new Fan(), new BrownNoise(), new PinkNoise()
            };
            // Loudness calibration, measured with --selftest so every sound sits at a similar level at 100%.
            float[] calib = { 1.6f, 2.2f, 2.0f, 2.3f, 1.75f, 1.5f, 1.6f, 1.05f, 0.9f, 0.77f };
            for (int i = 0; i < list.Count; i++) list[i].Calib = calib[i];
            return list;
        }
    }

    sealed class Rain : Sound
    {
        readonly Pink pl = new Pink(), pr = new Pink();
        readonly Biquad hl = new Biquad(Biquad.HP, 450, 0.7f), hr = new Biquad(Biquad.HP, 450, 0.7f);
        readonly Biquad ll = new Biquad(Biquad.LP, 6500, 0.7f), lr = new Biquad(Biquad.LP, 6500, 0.7f);
        readonly Biquad dl = new Biquad(Biquad.HP, 1800, 0.7f), dr = new Biquad(Biquad.HP, 1800, 0.7f);
        readonly Drift inten;
        readonly float[] dAmp = new float[64], dDec = new float[64], dPan = new float[64];
        int dCount;

        public Rain() : base("Rain", "\U0001F327", Color.FromArgb(112, 172, 255), 1) { inten = new Drift(rng, 0.7f, 1f, 3f, 8f, 2f); }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0) inten.Step();
                float k = inten.V;
                float bl = ll.P(hl.P(pl.Next(rng.W()))), br = lr.P(hr.P(pr.Next(rng.W())));
                if (dCount < 64 && rng.U() < 90f / Dsp.Fs * k)
                {
                    float a = rng.U();
                    dAmp[dCount] = 0.05f + 0.9f * a * a * a;
                    dDec[dCount] = (float)Math.Exp(-1.0 / (rng.Range(0.0008f, 0.004f) * Dsp.Fs));
                    dPan[dCount] = rng.U();
                    dCount++;
                }
                float sl = 0, sr = 0;
                for (int d = 0; d < dCount; d++)
                {
                    float s = rng.W() * dAmp[d];
                    sl += s * (1 - dPan[d]); sr += s * dPan[d];
                    dAmp[d] *= dDec[d];
                    if (dAmp[d] < 0.002f) { dCount--; dAmp[d] = dAmp[dCount]; dDec[d] = dDec[dCount]; dPan[d] = dPan[dCount]; d--; }
                }
                l[i] += bl * k + dl.P(sl) * 0.6f;
                r[i] += br * k + dr.P(sr) * 0.6f;
            }
        }
    }

    sealed class Thunder : Sound
    {
        readonly Brown bl = new Brown(), br = new Brown();
        readonly Biquad ll = new Biquad(Biquad.LP, 300, 0.7f), lr = new Biquad(Biquad.LP, 300, 0.7f), hc = new Biquad(Biquad.HP, 1500, 0.7f);
        readonly Drift roll;
        float env, envDec = 0.9999f, crack, crackDec, attackStep;
        int attackLeft, nextIn;

        public Thunder() : base("Thunder", ((char)0x26C8).ToString(), Color.FromArgb(182, 152, 255), 2)
        {
            roll = new Drift(rng, 0.2f, 1f, 0.12f, 0.6f, 0.1f);
            nextIn = (int)(2.5f * Dsp.Fs);
        }

        void Strike()
        {
            float near = rng.U();
            float peak = 0.35f + 0.65f * near, cut = 160 + 520 * near;
            ll.Set(Biquad.LP, cut, 0.7f); lr.Set(Biquad.LP, cut * 1.08f, 0.7f);
            attackLeft = (int)(rng.Range(0.08f, 0.5f) * (1.2f - near) * Dsp.Fs) + 1;
            attackStep = (peak - env) / attackLeft;
            envDec = (float)Math.Exp(-1.0 / (rng.Range(2.5f, 6f) * Dsp.Fs));
            crack = near > 0.7f ? peak * 0.6f : 0f;
            crackDec = (float)Math.Exp(-1.0 / (0.09 * Dsp.Fs));
            nextIn = (int)(rng.Range(10f, 35f) * Dsp.Fs);
        }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0) { roll.Step(); nextIn -= 64; if (nextIn <= 0) Strike(); }
                if (attackLeft > 0) { env += attackStep; attackLeft--; } else env *= envDec;
                float g = env * roll.V;
                float c = hc.P(rng.W()) * crack; crack *= crackDec;
                l[i] += ll.P(bl.Next(rng.W())) * g + c;
                r[i] += lr.P(br.Next(rng.W())) * g + c * 0.8f;
            }
        }
    }

    sealed class Wind : Sound
    {
        readonly Biquad bl = new Biquad(Biquad.BP, 500, 1.2f), br = new Biquad(Biquad.BP, 560, 1.2f);
        readonly Biquad rl = new Biquad(Biquad.LP, 180, 0.7f), rr = new Biquad(Biquad.LP, 180, 0.7f);
        readonly Brown nl = new Brown(), nr = new Brown();
        readonly Drift gust, jitter, spread;

        public Wind() : base("Wind", "\U0001F32C", Color.FromArgb(150, 222, 232), 3)
        {
            gust = new Drift(rng, 0.2f, 1f, 0.8f, 4f, 0.7f);
            jitter = new Drift(rng, -80f, 80f, 0.3f, 1.5f, 0.3f);
            spread = new Drift(rng, 0.85f, 1.2f, 2f, 6f, 2f);
        }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                {
                    gust.Step(); jitter.Step(); spread.Step();
                    float f = 200 + 850 * gust.V * gust.V + jitter.V; // stronger gusts whistle higher
                    bl.Set(Biquad.BP, f, 1.2f); br.Set(Biquad.BP, f * spread.V, 1.2f);
                }
                float g = gust.V;
                l[i] += (bl.P(rng.W()) + rl.P(nl.Next(rng.W())) * 0.6f) * g;
                r[i] += (br.P(rng.W()) + rr.P(nr.Next(rng.W())) * 0.6f) * g;
            }
        }
    }

    sealed class Ocean : Sound
    {
        readonly Brown bl = new Brown(), br = new Brown();
        readonly Biquad ll = new Biquad(Biquad.LP, 400, 0.7f), lr = new Biquad(Biquad.LP, 400, 0.7f);
        readonly Biquad fl = new Biquad(Biquad.HP, 2500, 0.7f), fr = new Biquad(Biquad.HP, 2500, 0.7f);
        float p = 0.6f, period = 9f, peak = 1f, eL, eR;

        public Ocean() : base("Ocean", "\U0001F30A", Color.FromArgb(72, 152, 232), 4) { }

        static float Env(float p)
        {
            if (p >= 1) p -= 1;
            return p < 0.28f ? Dsp.Smooth(p / 0.28f) : (float)Math.Exp(-(p - 0.28f) * 3.5f);
        }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                {
                    p += 64f / (period * Dsp.Fs);
                    if (p >= 1) { p -= 1; period = rng.Range(7f, 13f); peak = rng.Range(0.55f, 1f); }
                    eL = Env(p) * peak; eR = Env(p + 0.025f) * peak;
                    ll.Set(Biquad.LP, 220 + 2400 * eL * eL, 0.7f);
                    lr.Set(Biquad.LP, 220 + 2400 * eR * eR, 0.7f);
                }
                l[i] += ll.P(bl.Next(rng.W())) * (0.15f + 0.85f * eL) + fl.P(rng.W()) * 0.35f * eL * eL * eL;
                r[i] += lr.P(br.Next(rng.W())) * (0.15f + 0.85f * eR) + fr.P(rng.W()) * 0.35f * eR * eR * eR;
            }
        }
    }

    sealed class Brook : Sound
    {
        const int NV = 6;
        readonly Biquad[] bp = new Biquad[NV];
        readonly float[] f = new float[NV], ft = new float[NV], a = new float[NV], at = new float[NV], pan = new float[NV];
        readonly int[] left = new int[NV];
        readonly Pink pl = new Pink(), pr = new Pink();
        readonly Biquad ll = new Biquad(Biquad.LP, 1500, 0.7f), lr = new Biquad(Biquad.LP, 1500, 0.7f);

        public Brook() : base("Stream", "\U0001F4A7", Color.FromArgb(92, 212, 200), 5)
        {
            for (int v = 0; v < NV; v++) { f[v] = ft[v] = rng.Range(300, 2000); bp[v] = new Biquad(Biquad.BP, f[v], 6); pan[v] = rng.Range(0.15f, 0.85f); }
        }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0)
                {
                    for (int v = 0; v < NV; v++)
                    {
                        left[v] -= 64;
                        if (left[v] <= 0) { ft[v] = rng.Range(280, 2400); float u = rng.U(); at[v] = u * u; left[v] = (int)(rng.Range(0.012f, 0.09f) * Dsp.Fs); }
                        f[v] += (ft[v] - f[v]) * 0.3f;
                        a[v] += (at[v] - a[v]) * 0.25f;
                        bp[v].Set(Biquad.BP, f[v], 6);
                    }
                }
                float sl = 0, sr = 0;
                for (int v = 0; v < NV; v++) { float s = bp[v].P(rng.W()) * a[v]; sl += s * (1 - pan[v]); sr += s * pan[v]; }
                l[i] += sl + ll.P(pl.Next(rng.W())) * 0.5f;
                r[i] += sr + lr.P(pr.Next(rng.W())) * 0.5f;
            }
        }
    }

    sealed class Fire : Sound
    {
        readonly Brown bl = new Brown(), br = new Brown();
        readonly Biquad ll = new Biquad(Biquad.LP, 420, 0.7f), lr = new Biquad(Biquad.LP, 420, 0.7f);
        readonly Biquad hl = new Biquad(Biquad.HP, 900, 0.7f), hr = new Biquad(Biquad.HP, 900, 0.7f), hiss = new Biquad(Biquad.HP, 3500, 0.7f);
        readonly Drift flick, rate;
        const int Max = 96;
        readonly int[] cDelay = new int[Max];
        readonly float[] cAmp = new float[Max], cDec = new float[Max], cPan = new float[Max];
        int cCount;

        public Fire() : base("Fireplace", "\U0001F525", Color.FromArgb(255, 142, 72), 6)
        {
            flick = new Drift(rng, 0.45f, 1f, 0.06f, 0.35f, 0.06f);
            rate = new Drift(rng, 1.5f, 12f, 1f, 4f, 0.6f);
        }

        void Burst()
        {
            float u = rng.U();
            int clicks = 1 + (int)(u * u * 6);
            float t = 0, pan = rng.U();
            for (int k = 0; k < clicks && cCount < Max; k++)
            {
                float a = rng.U();
                cDelay[cCount] = (int)t;
                cAmp[cCount] = 0.1f + 0.9f * a * a * a;
                cDec[cCount] = (float)Math.Exp(-1.0 / (rng.Range(0.00015f, 0.0012f) * Dsp.Fs));
                cPan[cCount] = Math.Min(1, Math.Max(0, pan + rng.Range(-0.1f, 0.1f)));
                cCount++;
                t += rng.Range(0.001f, 0.02f) * Dsp.Fs;
            }
        }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((i & 63) == 0) { flick.Step(); rate.Step(); }
                if (rng.U() < rate.V / Dsp.Fs) Burst();
                float cl = 0, cr = 0;
                for (int c = 0; c < cCount; c++)
                {
                    if (cDelay[c] > 0) { cDelay[c]--; continue; }
                    float s = rng.W() * cAmp[c];
                    cl += s * (1 - cPan[c]); cr += s * cPan[c];
                    cAmp[c] *= cDec[c];
                    if (cAmp[c] < 0.001f)
                    {
                        cCount--;
                        cDelay[c] = cDelay[cCount]; cAmp[c] = cAmp[cCount]; cDec[c] = cDec[cCount]; cPan[c] = cPan[cCount];
                        c--;
                    }
                }
                float fl = flick.V, h = hiss.P(rng.W()) * 0.03f * fl;
                l[i] += ll.P(bl.Next(rng.W())) * fl * 0.9f + hl.P(cl) * 0.7f + h;
                r[i] += lr.P(br.Next(rng.W())) * fl * 0.9f + hr.P(cr) * 0.7f + h;
            }
        }
    }

    sealed class Night : Sound
    {
        sealed class Cricket { public float F, Ph, Pan, Amp, BasePeriod, Period, T, PulseLen, Cycle; public int Pulses; }
        readonly Cricket[] cs = new Cricket[5];
        readonly Brown bl = new Brown(), br = new Brown();
        readonly Biquad al = new Biquad(Biquad.LP, 350, 0.7f), ar = new Biquad(Biquad.LP, 350, 0.7f);

        public Night() : base("Crickets", "\U0001F319", Color.FromArgb(232, 212, 122), 7)
        {
            for (int i = 0; i < cs.Length; i++)
            {
                Cricket c = new Cricket
                {
                    F = rng.Range(4100, 5000), Pan = rng.Range(0.1f, 0.9f), Amp = rng.Range(0.25f, 1f),
                    BasePeriod = rng.Range(0.55f, 1.2f), Pulses = 2 + (int)(rng.U() * 3),
                    PulseLen = rng.Range(0.010f, 0.016f)
                };
                c.Cycle = c.PulseLen + rng.Range(0.006f, 0.012f);
                c.Period = c.BasePeriod; c.T = rng.Range(0, c.Period);
                cs[i] = c;
            }
        }

        public override void Render(float[] l, float[] r, int n)
        {
            const float dt = 1f / Dsp.Fs;
            for (int i = 0; i < n; i++)
            {
                float sl = 0, sr = 0;
                foreach (Cricket c in cs)
                {
                    c.T += dt;
                    if (c.T >= c.Period) { c.T -= c.Period; c.Period = c.BasePeriod * rng.Range(0.9f, 1.1f); }
                    c.Ph += Dsp.TwoPi * c.F * dt;
                    if (c.Ph > Dsp.TwoPi) c.Ph -= Dsp.TwoPi;
                    int k = (int)(c.T / c.Cycle);
                    if (k >= c.Pulses) continue;
                    float x = (c.T - k * c.Cycle) / c.PulseLen;
                    if (x >= 1) continue;
                    float e = (float)Math.Sin(Math.PI * x); e *= e;
                    float v = (float)Math.Sin(c.Ph) * e * c.Amp;
                    sl += v * (1 - c.Pan); sr += v * c.Pan;
                }
                l[i] += sl * 0.35f + al.P(bl.Next(rng.W())) * 0.15f;
                r[i] += sr * 0.35f + ar.P(br.Next(rng.W())) * 0.15f;
            }
        }
    }

    sealed class Fan : Sound
    {
        readonly Pink pl = new Pink(), pr = new Pink();
        readonly Brown bl = new Brown(), br = new Brown();
        readonly Biquad ll = new Biquad(Biquad.LP, 600, 0.7f), lr = new Biquad(Biquad.LP, 600, 0.7f);
        double ph, hph;

        public Fan() : base("Fan", "\U0001F300", Color.FromArgb(172, 182, 204), 8) { }

        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float am = 1 + 0.05f * (float)Math.Sin(ph);
                float hum = 0.015f * (float)Math.Sin(hph) + 0.008f * (float)Math.Sin(2 * hph);
                ph += 2 * Math.PI * 7.2 / Dsp.Fs; if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
                hph += 2 * Math.PI * 100 / Dsp.Fs; if (hph > 2 * Math.PI) hph -= 2 * Math.PI;
                l[i] += (ll.P(pl.Next(rng.W())) + bl.Next(rng.W()) * 0.4f) * am + hum;
                r[i] += (lr.P(pr.Next(rng.W())) + br.Next(rng.W()) * 0.4f) * am + hum;
            }
        }
    }

    sealed class BrownNoise : Sound
    {
        readonly Brown bl = new Brown(), br = new Brown();
        public BrownNoise() : base("Brown noise", "\U0001F342", Color.FromArgb(208, 146, 96), 9) { }
        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++) { l[i] += bl.Next(rng.W()); r[i] += br.Next(rng.W()); }
        }
    }

    sealed class PinkNoise : Sound
    {
        readonly Pink pl = new Pink(), pr = new Pink();
        public PinkNoise() : base("Pink noise", "\U0001F338", Color.FromArgb(242, 142, 192), 10) { }
        public override void Render(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++) { l[i] += pl.Next(rng.W()); r[i] += pr.Next(rng.W()); }
        }
    }

    // ------------------------------------------------------------------ audio output (waveOut, event driven)

    sealed class Engine
    {
        public readonly List<Sound> Sounds;
        public volatile bool Playing = true;
        public float Master = 0.8f, Fade = 1f;
        public string Error;
        const int Frames = 2048, NBuf = 4;
        readonly float[] L = new float[Frames], R = new float[Frames], TL = new float[Frames], TR = new float[Frames];
        readonly short[] pcm = new short[Frames * 2];
        float playGain, masterCur;
        volatile bool quit;
        Thread thread;

        public Engine(List<Sound> sounds) { Sounds = sounds; }

        public void Start()
        {
            thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "audio" };
            thread.Start();
        }

        public void Stop()
        {
            quit = true;
            if (thread != null) thread.Join(1000);
        }

        void Loop()
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            Native.WAVEFORMATEX fmt = new Native.WAVEFORMATEX
            {
                wFormatTag = 1, nChannels = 2, nSamplesPerSec = (uint)Dsp.Fs, nAvgBytesPerSec = (uint)Dsp.Fs * 4, nBlockAlign = 4, wBitsPerSample = 16
            };
            IntPtr hwo;
            int rc = Native.waveOutOpen(out hwo, -1, ref fmt, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000 /* CALLBACK_EVENT */);
            if (rc != 0) { Error = "No audio output device available (error " + rc + ")."; return; }

            int hdrSize = Marshal.SizeOf(typeof(Native.WAVEHDR));
            int flagsOff = (int)Marshal.OffsetOf(typeof(Native.WAVEHDR), "dwFlags");
            IntPtr[] hdrs = new IntPtr[NBuf], data = new IntPtr[NBuf];
            for (int b = 0; b < NBuf; b++)
            {
                data[b] = Marshal.AllocHGlobal(Frames * 4);
                hdrs[b] = Marshal.AllocHGlobal(hdrSize);
                Native.WAVEHDR h = new Native.WAVEHDR { lpData = data[b], dwBufferLength = Frames * 4 };
                Marshal.StructureToPtr(h, hdrs[b], false);
                Native.waveOutPrepareHeader(hwo, hdrs[b], hdrSize);
                Fill(data[b]);
                Native.waveOutWrite(hwo, hdrs[b], hdrSize);
            }
            while (!quit)
            {
                ev.WaitOne(100);
                for (int b = 0; b < NBuf && !quit; b++)
                {
                    if ((Marshal.ReadInt32(hdrs[b], flagsOff) & 1 /* WHDR_DONE */) == 0) continue;
                    Fill(data[b]);
                    Native.waveOutWrite(hwo, hdrs[b], hdrSize);
                }
            }
            Native.waveOutReset(hwo);
            for (int b = 0; b < NBuf; b++)
            {
                Native.waveOutUnprepareHeader(hwo, hdrs[b], hdrSize);
                Marshal.FreeHGlobal(hdrs[b]); Marshal.FreeHGlobal(data[b]);
            }
            Native.waveOutClose(hwo);
        }

        public void Mix(float[] outL, float[] outR, int n)
        {
            Array.Clear(outL, 0, n); Array.Clear(outR, 0, n);
            float pgTarget = Playing ? 1f : 0f;
            bool silent = pgTarget == 0f && playGain <= 0f;
            float view = playGain * (float)Math.Sqrt(Math.Max(masterCur, 0));
            foreach (Sound s in Sounds)
            {
                float target = s.On ? s.Vol * s.Vol * s.Calib : 0f;
                if (silent || (target <= 0f && s.Cur < 1e-6f)) { if (target <= 0f) s.Cur = 0; s.Level = 0; continue; }
                Array.Clear(TL, 0, n); Array.Clear(TR, 0, n);
                s.Render(TL, TR, n);
                float g0 = s.Cur, g1 = g0 + (target - g0) * 0.3f;
                if (Math.Abs(target - g1) < 1e-6f) g1 = target;
                double ss = 0;
                for (int i = 0; i < n; i++)
                {
                    float g = g0 + (g1 - g0) * i / n;
                    float a = TL[i] * g, b = TR[i] * g;
                    outL[i] += a; outR[i] += b; ss += a * a + b * b;
                }
                s.Cur = g1;
                s.Level = (float)Math.Sqrt(ss / (2 * n)) * view;
            }
            float mTarget = Master * Master * Fade, pgStep = 1f / (0.8f * Dsp.Fs);
            for (int i = 0; i < n; i++)
            {
                if (playGain < pgTarget) playGain = Math.Min(pgTarget, playGain + pgStep);
                else if (playGain > pgTarget) playGain = Math.Max(pgTarget, playGain - pgStep);
                masterCur += (mTarget - masterCur) * 0.0005f;
                float g = Dsp.Smooth(playGain) * masterCur;
                outL[i] = Soft(outL[i] * g); outR[i] = Soft(outR[i] * g);
            }
        }

        void Fill(IntPtr dst)
        {
            Mix(L, R, Frames);
            for (int i = 0; i < Frames; i++) { pcm[2 * i] = (short)(L[i] * 32767f); pcm[2 * i + 1] = (short)(R[i] * 32767f); }
            Marshal.Copy(pcm, 0, dst, Frames * 2);
        }

        static float Soft(float x) // gentle limiter: transparent below 0.85, never clips
        {
            float a = Math.Abs(x);
            if (a <= 0.85f) return x;
            float y = 0.85f + 0.15f * (float)Math.Tanh((a - 0.85f) / 0.15f);
            return x < 0 ? -y : y;
        }
    }


    // ------------------------------------------------------------------ mixes

    static class Presets
    {
        public static readonly string[] Names = { "Deep focus", "Rainy cabin", "Beach", "Campfire night", "Forest stream", "Thunderstorm", "Sleep" };
        static readonly string[][] Mix =
        {
            new[] { "Brown noise:0.6", "Rain:0.35" },
            new[] { "Rain:0.6", "Fireplace:0.55", "Thunder:0.35" },
            new[] { "Ocean:0.75", "Wind:0.3" },
            new[] { "Fireplace:0.65", "Crickets:0.45", "Wind:0.2" },
            new[] { "Stream:0.6", "Wind:0.25", "Pink noise:0.15" },
            new[] { "Rain:0.75", "Thunder:0.65", "Wind:0.45" },
            new[] { "Fan:0.5", "Pink noise:0.3", "Rain:0.25" },
        };

        /// <summary>Turns on exactly the sounds in mix <paramref name="p"/>, at its volumes.</summary>
        public static void Apply(int p, List<Sound> sounds)
        {
            foreach (Sound s in sounds) s.On = false;
            foreach (string item in Mix[p])
            {
                string[] kv = item.Split(':');
                foreach (Sound s in sounds)
                    if (s.Name == kv[0]) { s.Vol = float.Parse(kv[1], CultureInfo.InvariantCulture); s.On = true; }
            }
        }
    }
}

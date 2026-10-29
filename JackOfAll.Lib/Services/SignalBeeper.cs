using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JackOfAll.Lib.Services;

/// <summary>
/// Metal-detector style feedback for the Wi-Fi hunt: beeps get faster and higher as the signal gets
/// stronger. Tones are synthesized once and played from memory through winmm.
/// </summary>
public sealed class SignalBeeper : IDisposable
{
    private const uint SndSync = 0x0000, SndMemory = 0x0004, SndNoDefault = 0x0002;
    private readonly byte[][] _tones = new byte[12][];
    private readonly CancellationTokenSource _cts = new();

    public bool Enabled { get; set; }

    /// <summary>Current signal in dBm; below -99 means "no signal" and stays silent.</summary>
    public float Dbm { get; set; } = -100;

    public SignalBeeper()
    {
        for (var i = 0; i < _tones.Length; i++) _tones[i] = Tone(330 * Math.Pow(2, i * 2 / 12.0));
        var thread = new Thread(Loop) { IsBackground = true, Name = "signal-beeper" };
        thread.Start();
    }

    public void Dispose() => _cts.Cancel();

    private void Loop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var d = Dbm;
            if (!Enabled || d < -99)
            {
                ct.WaitHandle.WaitOne(80);
                continue;
            }
            var t = Math.Clamp((d + 90) / 55f, 0, 1);
            PlaySound(_tones[(int)(t * 11.99f)], 0, SndSync | SndMemory | SndNoDefault);
            ct.WaitHandle.WaitOne((int)(950 - 860 * t));
        }
    }

    private static byte[] Tone(double freq)
    {
        const int Rate = 22050, N = Rate * 55 / 1000;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + N * 2);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(N * 2);
        for (var i = 0; i < N; i++)
        {
            double t = (double)i / Rate, env = Math.Min(1, i / 60.0) * Math.Exp(-t * 55);
            var x = Math.Sin(2 * Math.PI * freq * t) + 0.25 * Math.Sin(4 * Math.PI * freq * t);
            w.Write((short)(x * env * 9000));
        }
        w.Flush();
        return ms.ToArray();
    }

    [DllImport("winmm.dll")]
    private static extern bool PlaySound(byte[] sound, nint module, uint flags);
}

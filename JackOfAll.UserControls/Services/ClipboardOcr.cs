using System.IO;
using System.Threading.Channels;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace JackOfAll.UserControls.Services;

/// <summary>Reads the text in copied images with the OCR engine built into Windows, one image at a time in the background.</summary>
internal sealed class ClipboardOcr
{
    private readonly Channel<ClipEntry> _queue = Channel.CreateUnbounded<ClipEntry>();
    private readonly Dispatcher _dispatcher;
    private readonly Action<ClipEntry, string> _done;

    public ClipboardOcr(Dispatcher dispatcher, Action<ClipEntry, string> done)
    {
        _dispatcher = dispatcher;
        _done = done;
        _ = Task.Run(RunAsync);
    }

    public void Enqueue(ClipEntry clip) => _queue.Writer.TryWrite(clip);

    private async Task RunAsync()
    {
        OcrEngine? engine = null;
        try { engine = OcrEngine.TryCreateFromUserProfileLanguages(); }
        catch { /* OCR isn't available on this edition of Windows */ }

        await foreach (var clip in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var text = "";
            if (engine is not null)
            {
                try { text = await RecognizeAsync(engine, clip.ImagePath).ConfigureAwait(false); }
                catch { text = ""; }
            }
            _ = _dispatcher.BeginInvoke(() => _done(clip, text));
        }
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        // Small screenshots read better at twice the size; large ones must fit within the engine's limit.
        uint width = decoder.PixelWidth, height = decoder.PixelHeight, longest = Math.Max(width, height);
        var scale = longest < 1000 ? 2.0 : longest > OcrEngine.MaxImageDimension ? (double)OcrEngine.MaxImageDimension / longest : 1.0;
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, width * scale),
            ScaledHeight = (uint)Math.Max(1, height * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(static line => line.Text)).Trim();
    }
}

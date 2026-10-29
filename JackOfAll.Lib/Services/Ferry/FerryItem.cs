using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JackOfAll.Lib.Services.Ferry;

/// <summary>One thing shared between the phone and the PC: a file (either direction) or a piece of text.</summary>
public sealed partial class FerryItem : ObservableObject
{
    private readonly object _thumbGate = new();
    private bool _thumbTried;

    public int Id { get; internal set; }
    public DateTime Time { get; internal set; }
    public bool IsText { get; init; }
    public bool ToPhone { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Text { get; init; } = "";
    public string Device { get; init; } = "";
    public long Size { get; init; }

    [ObservableProperty] private int _downloads;
    [ObservableProperty] private BitmapSource? _thumbnail;

    /// <summary>JPEG thumbnail served to the phone; made together with <see cref="Thumbnail"/>.</summary>
    internal byte[]? ThumbJpeg { get; private set; }

    public bool CanThumb => !IsText && FerryMime.Decodable(Name);

    public string Title => IsText ? Squash(Text, 400) : Name;
    public string Direction => ToPhone ? "To phone" : $"From {Device}";
    public string Meta => (IsText ? "" : $"  ·  {FerryMime.FormatSize(Size)}") + $"  ·  {Time:t}";
    public string Badge => IsText ? "Aa" : Path.Length > 0 && Directory.Exists(Path) ? "DIR" : Extension(Name);
    public string Status => !ToPhone ? "" : Downloads > 0 ? "Saved on phone" : "Ready on phone";

    partial void OnDownloadsChanged(int value) => OnPropertyChanged(nameof(Status));

    /// <summary>Decodes a small preview once (photos only). Safe to call from any thread.</summary>
    public void EnsureThumb()
    {
        lock (_thumbGate)
        {
            if (_thumbTried || !CanThumb) return;
            _thumbTried = true;
            try
            {
                var thumb = Decode(Path, 320);
                var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
                encoder.Frames.Add(BitmapFrame.Create(thumb));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                ThumbJpeg = stream.ToArray();
                Thumbnail = thumb;
            }
            catch
            {
                // Not an image after all, or a format Windows can't decode: the phone and the PC show the file type instead.
            }
        }
    }

    // Photos from phones are stored sideways with an EXIF orientation tag; apply it so thumbnails stand upright.
    private static BitmapSource Decode(string path, int maxSide)
    {
        var bytes = File.ReadAllBytes(path);
        var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.GetQuery("System.Photo.Orientation") is ushort value) orientation = value;
        }
        catch { }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(maxSide, frame.PixelWidth);
        else image.DecodePixelHeight = Math.Min(maxSide, frame.PixelHeight);
        image.EndInit();

        BitmapSource result = image;
        var angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angle != 0) result = new TransformedBitmap(image, new RotateTransform(angle));
        // Re-create as plain pixels so the frozen bitmap can be used and encoded from any thread.
        var converted = new FormatConvertedBitmap(result, PixelFormats.Bgr32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var plain = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        plain.Freeze();
        return plain;
    }

    private static string Extension(string name)
    {
        var ext = System.IO.Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        return ext.Length is > 0 and <= 4 ? ext : "FILE";
    }

    private static string Squash(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > max ? flat[..max] + "…" : flat;
    }
}

public static class FerryMime
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".bmp"] = "image/bmp", [".heic"] = "image/heic", [".svg"] = "image/svg+xml", [".mp4"] = "video/mp4", [".mov"] = "video/quicktime",
        [".webm"] = "video/webm", [".m4v"] = "video/mp4", [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg", [".pdf"] = "application/pdf", [".txt"] = "text/plain; charset=utf-8", [".zip"] = "application/zip",
    };

    public static string Of(string name) => Types.TryGetValue(Path.GetExtension(name), out var type) ? type : "application/octet-stream";

    /// <summary>Only media, PDFs and plain text open in the phone's browser; everything else downloads, so nothing can run as the page.</summary>
    public static bool Inline(string name)
    {
        var type = Of(name);
        return (type.StartsWith("image/") && type != "image/svg+xml") || type.StartsWith("video/") || type.StartsWith("audio/")
            || type == "application/pdf" || type.StartsWith("text/plain");
    }

    public static bool Decodable(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = -1;
        do { value /= 1024; i++; } while (value >= 1024 && i < units.Length - 1);
        return (value < 10 ? value.ToString("0.0") : Math.Round(value).ToString()) + " " + units[i];
    }
}

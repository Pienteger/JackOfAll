using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls;

public enum ClipKind { Text, Image, Files }

public enum ClipFilter { All, Pinned, Text, Images, Files }

/// <summary>One clipboard history item: text (with its formatting), an image (with the text read from it) or a list of files.</summary>
public sealed partial class ClipEntry : ObservableObject
{
    private static readonly Brush AccentBar = CreateFrozenBrush(Color.FromRgb(0x63, 0x66, 0xf1));
    private static readonly Brush TransparentBar = Brushes.Transparent;

    private string? _haystack;
    private bool _thumbnailRequested;
    private BitmapSource? _previewImage;

    public ClipKind Kind { get; init; }

    /// <summary>The text for a text clip; the file paths, one per line, for a files clip; empty for an image.</summary>
    public string Content { get; init; } = "";
    public string Html { get; set; } = "";
    public string Rtf { get; set; } = "";
    public string ImageId { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string[] Files { get; init; } = [];

    [ObservableProperty] private DateTime _copiedAt;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _ocrText = "";
    [ObservableProperty] private bool _ocrDone;
    [ObservableProperty] private int _uses;
    [ObservableProperty] private BitmapSource? _loadedThumbnail;

    public string ImagePath => Path.Combine(ClipboardHistoryService.ImageDir, ImageId + ".png");
    public bool IsText => Kind == ClipKind.Text;
    public bool IsImage => Kind == ClipKind.Image;
    public bool IsFiles => Kind == ClipKind.Files;
    public bool HasFormatting => Html.Length > 0 || Rtf.Length > 0;
    public bool IsLink => IsText && LooksLikeLink(Content);

    public bool Matches(ClipFilter filter) => filter switch
    {
        ClipFilter.Pinned => IsPinned,
        ClipFilter.Text => IsText,
        ClipFilter.Images => IsImage,
        ClipFilter.Files => IsFiles,
        _ => true,
    };

    /// <summary>Lower-cased text that search looks through: the clip itself, text read from an image, file names and the source app.</summary>
    public string Haystack => _haystack ??= BuildHaystack();

    private string BuildHaystack()
    {
        var builder = new StringBuilder();
        builder.Append(Content.Length > 50_000 ? Content[..50_000] : Content).Append('\n').Append(OcrText).Append('\n').Append(Source);
        if (IsImage) builder.Append("\nimage screenshot picture");
        return builder.ToString().ToLowerInvariant();
    }

    partial void OnOcrTextChanged(string value)
    {
        _haystack = null;
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OcrPreview));
    }

    partial void OnOcrDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(OcrPreview));
    }

    partial void OnSourceChanged(string value)
    {
        _haystack = null;
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(DetailText));
    }

    partial void OnCopiedAtChanged(DateTime value)
    {
        OnPropertyChanged(nameof(TimestampText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(DetailText));
    }

    partial void OnUsesChanged(int value) => OnPropertyChanged(nameof(StatsText));

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinGlyph));
        OnPropertyChanged(nameof(PinTooltip));
        OnPropertyChanged(nameof(PinForeground));
        OnPropertyChanged(nameof(PinBarBrush));
    }

    // ── list row ────────────────────────────────────────────────

    public string Glyph => Kind switch
    {
        ClipKind.Files => Files.Length == 1 && Directory.Exists(Files[0]) ? "" : "",
        _ => IsLink ? "" : "",
    };

    public string Title => Kind switch
    {
        ClipKind.Image => $"Image · {Width} × {Height}",
        ClipKind.Files => FileName(Files.FirstOrDefault() ?? "") + (Files.Length > 1 ? $"  +{Files.Length - 1} more" : ""),
        _ => Squash(Content, 300),
    };

    public string Subtitle => Kind switch
    {
        ClipKind.Image => OcrText.Length > 0 ? Squash(OcrText, 200) : OcrDone ? "No text in this image" : "Reading the text in the image…",
        ClipKind.Files => Path.GetDirectoryName(Files.FirstOrDefault()?.TrimEnd('\\') ?? "") ?? "",
        _ => "",
    };

    public bool HasSubtitle => !IsText;

    public string TimestampText => CopiedAt.Date == DateTime.Today
        ? CopiedAt.ToString("HH:mm:ss")
        : CopiedAt.ToString("MMM dd, HH:mm");

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (Source.Length > 0) parts.Add(Source);
            parts.Add(TimestampText);
            if (IsText) parts.Add(SizeText + LineCountText);
            return string.Join("  ·  ", parts);
        }
    }

    public string SizeText
    {
        get
        {
            var bytes = Encoding.UTF8.GetByteCount(Content);
            return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:F1} KB";
        }
    }

    public string LineCountText
    {
        get
        {
            var lines = Content.Count(static character => character == '\n') + 1;
            return lines > 1 ? $"  |  {lines} lines" : "";
        }
    }

    /// <summary>A small version of the image for list rows, decoded off the UI thread the first time a row asks for it.</summary>
    public BitmapSource? Thumbnail
    {
        get
        {
            if (!IsImage || LoadedThumbnail is not null || _thumbnailRequested) return LoadedThumbnail;
            _thumbnailRequested = true;
            var path = ImagePath;
            _ = Task.Run(() => Decode(path, 160)).ContinueWith(task =>
            {
                LoadedThumbnail = task.Result;
                OnPropertyChanged(nameof(Thumbnail));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }
    }

    // ── preview pane ───────────────────────────────────────────

    public string KindLabel => Kind switch
    {
        ClipKind.Image => "Image",
        ClipKind.Files => Files.Length == 1 ? "File" : $"{Files.Length} files",
        _ => IsLink ? "Link" : "Text",
    };

    public string DetailText => CopiedAt.ToString("ddd d MMM, HH:mm") + (Source.Length > 0 ? $"  ·  from {Source}" : "");

    public string PreviewText => Content.Length > 20_000 ? Content[..20_000] + "\n…" : Content;

    public bool IsCodeLike => IsText && Content.Contains('\n') &&
        (Content.Contains("    ") || Content.Contains('\t') || Content.Contains('{') || Content.Contains(";\n"));

    public string OcrPreview => OcrText.Length > 0 ? OcrText : OcrDone ? "No text found." : "Reading…";

    public BitmapSource? PreviewImage => IsImage ? _previewImage ??= Decode(ImagePath, 1200) : null;

    public IEnumerable<ClipFileItem> FileItems => Files.Select(static f => new ClipFileItem(f));

    public string StatsText
    {
        get
        {
            var used = Uses > 0 ? $"  ·  used {Uses} {(Uses == 1 ? "time" : "times")}" : "";
            return Kind switch
            {
                ClipKind.Image => $"{Width} × {Height} px{used}",
                ClipKind.Files => $"Pastes as files{used}",
                _ => $"{Content.Length:N0} characters  ·  {Content.Count(static c => c == '\n') + 1} lines" + (HasFormatting ? "  ·  keeps formatting" : "") + used,
            };
        }
    }

    // ── pin button ─────────────────────────────────────────────

    public string PinGlyph => IsPinned ? "" : "";
    public string PinTooltip => IsPinned ? "Unpin" : "Pin";
    public Brush PinForeground => IsPinned ? AccentBar : TransparentBar;
    public Brush PinBarBrush => IsPinned ? AccentBar : TransparentBar;

    // ── helpers ────────────────────────────────────────────────

    internal static BitmapSource? Decode(string path, int maxSide)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (frame.PixelWidth >= frame.PixelHeight) { if (frame.PixelWidth > maxSide) image.DecodePixelWidth = maxSide; }
            else if (frame.PixelHeight > maxSide) image.DecodePixelHeight = maxSide;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string FileName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return name.Length > 0 ? name : path;
    }

    private static bool LooksLikeLink(string text)
    {
        var trimmed = text.Trim();
        return !trimmed.Contains(' ') && !trimmed.Contains('\n') &&
            (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
    }

    private static string Squash(string text, int max)
    {
        var builder = new StringBuilder();
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = builder.Length > 0; continue; }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(character);
            if (builder.Length >= max) { builder.Append('…'); break; }
        }
        return builder.ToString();
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class ClipFileItem(string path)
{
    public string Path { get; } = path;
    public bool Exists { get; } = File.Exists(path) || Directory.Exists(path);
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;
    public string Folder => (Exists ? "" : "Missing  ·  ") + (System.IO.Path.GetDirectoryName(Path.TrimEnd('\\')) ?? "");
    public double Opacity => Exists ? 1.0 : 0.55;
}

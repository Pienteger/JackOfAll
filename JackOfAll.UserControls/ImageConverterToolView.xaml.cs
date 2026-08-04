using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class ImageConverterToolView : UserControl, IStatefulTool
{
    public ImageConverterToolView()
    {
        InitializeComponent();
    }

    private void OnBrowseInputClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.heic;*.heif|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            InputPathBox.Text = dlg.FileName;
            SetDefaultOutputPath();
            LoadPreview(dlg.FileName);
        }
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var format = GetFormat();
        var dlg = new SaveFileDialog
        {
            Filter = $"{format.ToUpperInvariant()} file (*.{format})|*.{format}|All files (*.*)|*.*",
            FileName = MakeOutputFileName(InputPathBox.Text.Trim(), format)
        };

        if (dlg.ShowDialog() == true)
            OutputPathBox.Text = dlg.FileName;
    }

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !string.IsNullOrWhiteSpace(InputPathBox.Text))
            SetDefaultOutputPath();
    }

    private void OnConvertClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = InputPathBox.Text.Trim();
            var output = OutputPathBox.Text.Trim();
            if (!File.Exists(input))
            {
                SetStatus("Source image was not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                SetDefaultOutputPath();
                output = OutputPathBox.Text.Trim();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            var bitmap = LoadBitmap(input);
            var format = GetFormat();
            if (format == "ico")
                SaveIco(bitmap, output, GetIconSize());
            else
                SaveWithEncoder(bitmap, output, format);

            SetStatus($"Converted to {output}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void LoadPreview(string path)
    {
        try
        {
            PreviewImage.Source = LoadBitmap(path);
            SetStatus("Preview loaded.");
        }
        catch (Exception ex)
        {
            PreviewImage.Source = null;
            SetStatus($"Preview unavailable: {ex.Message}");
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private void SaveWithEncoder(BitmapSource source, string output, string format)
    {
        BitmapEncoder encoder = format switch
        {
            "jpg" => new JpegBitmapEncoder { QualityLevel = GetJpegQuality() },
            "bmp" => new BmpBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(ConvertForEncoder(source, format)));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }

    private static BitmapSource ConvertForEncoder(BitmapSource source, string format)
    {
        if (format is not ("jpg" or "bmp"))
            return source;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }

        var rendered = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static void SaveIco(BitmapSource source, string output, int size)
    {
        var scaled = ResizeBitmap(source, size, size);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        var png = pngStream.ToArray();

        using var writer = new BinaryWriter(File.Create(output));
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(22);
        writer.Write(png);
    }

    private static BitmapSource ResizeBitmap(BitmapSource source, int width, int height)
    {
        var scaleX = width / (double)source.PixelWidth;
        var scaleY = height / (double)source.PixelHeight;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
        transformed.Freeze();
        return transformed;
    }

    private void SetDefaultOutputPath()
    {
        var input = InputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;

        OutputPathBox.Text = Path.Combine(
            Path.GetDirectoryName(input) ?? string.Empty,
            MakeOutputFileName(input, GetFormat()));
    }

    private static string MakeOutputFileName(string input, string format)
    {
        var baseName = string.IsNullOrWhiteSpace(input) ? "converted" : Path.GetFileNameWithoutExtension(input);
        return $"{baseName}.{format}";
    }

    private string GetFormat() => (((ComboBoxItem)FormatBox.SelectedItem).Content?.ToString() ?? "png").ToLowerInvariant();

    private int GetJpegQuality()
        => int.TryParse(QualityBox.Text.Trim(), out var quality) ? Math.Clamp(quality, 1, 100) : 90;

    private int GetIconSize()
        => int.TryParse(((ComboBoxItem)IconSizeBox.SelectedItem).Content?.ToString(), out var size) ? size : 64;

    private void OnOpenOutputClick(object sender, RoutedEventArgs e)
    {
        var output = OutputPathBox.Text.Trim();
        var folder = File.Exists(output) ? Path.GetDirectoryName(output) : output;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void SetStatus(string message) => StatusText.Text = message;

    public Dictionary<string, string> SaveState() => new()
    {
        ["input"] = InputPathBox.Text,
        ["output"] = OutputPathBox.Text,
        ["format"] = FormatBox.SelectedIndex.ToString(),
        ["quality"] = QualityBox.Text,
        ["iconSize"] = IconSizeBox.SelectedIndex.ToString()
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("input", out var input)) InputPathBox.Text = input;
        if (state.TryGetValue("output", out var output)) OutputPathBox.Text = output;
        if (state.TryGetValue("format", out var format) && int.TryParse(format, out var formatIndex) && formatIndex >= 0 && formatIndex < FormatBox.Items.Count) FormatBox.SelectedIndex = formatIndex;
        if (state.TryGetValue("quality", out var quality)) QualityBox.Text = quality;
        if (state.TryGetValue("iconSize", out var iconSize) && int.TryParse(iconSize, out var iconIndex) && iconIndex >= 0 && iconIndex < IconSizeBox.Items.Count) IconSizeBox.SelectedIndex = iconIndex;
        if (File.Exists(InputPathBox.Text.Trim()))
            LoadPreview(InputPathBox.Text.Trim());
    }
}
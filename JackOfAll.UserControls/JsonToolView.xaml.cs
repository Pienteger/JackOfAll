using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class JsonToolView : UserControl, IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    public JsonToolView(IMessageBus bus, string? initialJson = null)
    {
        InitializeComponent();

        _subscription = bus.Subscribe<JsonDetectedMessage>(OnJsonDetected);

        if (initialJson is not null)
        {
            InputBox.Text = initialJson;
            Format();
        }
    }

    private void OnJsonDetected(JsonDetectedMessage msg)
    {
    }

    private void OnFormatClick(object sender, RoutedEventArgs e) => Format();
    private void OnMinifyClick(object sender, RoutedEventArgs e) => Minify();
    private void OnEscapeClick(object sender, RoutedEventArgs e) => Escape();
    private void OnUnescapeClick(object sender, RoutedEventArgs e) => Unescape();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        InputBox.Clear();
        OutputBox.Clear();
        SetStatus("Paste JSON to get started.", "SurfaceBackground", "TextSecondary");
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
        {
            Clipboard.SetText(OutputBox.Text);
            SetStatus("Copied to clipboard.", "SuccessBackground", "SuccessText");
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Paste JSON to get started.", "SurfaceBackground", "TextSecondary");
            return;
        }

        var error = JsonService.Validate(text);
        if (error is null)
            SetStatus("Valid JSON", "SuccessBackground", "SuccessText");
        else
            SetStatus($"Invalid: {error}", "ErrorBackground", "ErrorText");
    }

    private void Format()
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Format(input);
        if (result is not null)
        {
            OutputBox.Text = result;
            SetStatus("Formatted.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void Minify()
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Minify(input);
        if (result is not null)
        {
            OutputBox.Text = result;
            SetStatus("Minified.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void Escape()
    {
        var input = InputBox.Text;
        if (string.IsNullOrEmpty(input)) return;

        OutputBox.Text = JsonService.Escape(input);
        SetStatus("Escaped.", "SuccessBackground", "SuccessText");
    }

    private void Unescape()
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Unescape(input);
        if (result is not null)
        {
            OutputBox.Text = result;
            SetStatus("Unescaped.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void SetStatus(string message, string backgroundKey, string foregroundKey)
    {
        StatusText.Text = message;
        StatusBorder.Background = FindBrush(backgroundKey);
        StatusText.Foreground = FindBrush(foregroundKey);
    }

    private Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.Dispose();
    }
}

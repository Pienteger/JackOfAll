using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class Base64ToolView : UserControl
{
    public Base64ToolView()
    {
        InitializeComponent();
    }

    private void OnEncodeClick(object sender, RoutedEventArgs e)
    {
        var (result, error) = Base64Service.Encode(InputBox.Text);
        SetOutput(result, error, "Encoded");
    }

    private void OnDecodeClick(object sender, RoutedEventArgs e)
    {
        var (result, error) = Base64Service.Decode(InputBox.Text);
        SetOutput(result, error, "Decoded");
    }

    private void OnUrlEncodeClick(object sender, RoutedEventArgs e)
    {
        var (result, error) = Base64Service.UrlEncode(InputBox.Text);
        SetOutput(result, error, "URL-safe encoded (no padding)");
    }

    private void OnUrlDecodeClick(object sender, RoutedEventArgs e)
    {
        var (result, error) = Base64Service.UrlDecode(InputBox.Text);
        SetOutput(result, error, "URL-safe decoded");
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        InputBox.Text = string.Empty;
        OutputBox.Text = string.Empty;
        StatusText.Text = "Paste text or Base64 to convert.";
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
            Clipboard.SetText(OutputBox.Text);
    }

    private void SetOutput(string? result, string? error, string successMessage)
    {
        if (error != null)
        {
            OutputBox.Text = string.Empty;
            StatusText.Text = error;
        }
        else
        {
            OutputBox.Text = result;
            StatusText.Text = $"{successMessage} ({result?.Length ?? 0} chars)";
        }
    }
}

using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class TextEncodeToolView : UserControl, IStatefulTool
{
    public TextEncodeToolView()
    {
        InitializeComponent();
    }

    private void OnUrlEncode(object sender, RoutedEventArgs e)
        => OutputBox.Text = Uri.EscapeDataString(InputBox.Text);

    private void OnUrlDecode(object sender, RoutedEventArgs e)
        => OutputBox.Text = Uri.UnescapeDataString(InputBox.Text.Replace("+", " "));

    private void OnHtmlEncode(object sender, RoutedEventArgs e)
        => OutputBox.Text = WebUtility.HtmlEncode(InputBox.Text);

    private void OnHtmlDecode(object sender, RoutedEventArgs e)
        => OutputBox.Text = WebUtility.HtmlDecode(InputBox.Text);

    private void OnJsonEscape(object sender, RoutedEventArgs e)
        => OutputBox.Text = JsonSerializer.Serialize(InputBox.Text)[1..^1];

    private void OnJsonUnescape(object sender, RoutedEventArgs e)
    {
        try
        {
            OutputBox.Text = JsonSerializer.Deserialize<string>($"\"{InputBox.Text}\"") ?? string.Empty;
        }
        catch (JsonException ex)
        {
            OutputBox.Text = ex.Message;
        }
    }

    private void OnCopyOutput(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
            Clipboard.SetText(OutputBox.Text);
    }

    public Dictionary<string, string> SaveState() => new()
    {
        ["input"] = InputBox.Text,
        ["output"] = OutputBox.Text
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("input", out var input)) InputBox.Text = input;
        if (state.TryGetValue("output", out var output)) OutputBox.Text = output;
    }
}
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class HashToolView : UserControl, IStatefulTool
{
    public HashToolView()
    {
        InitializeComponent();
    }

    private void OnHashClick(object sender, RoutedEventArgs e) => ComputeHashes();

    private void OnInputChanged(object sender, TextChangedEventArgs e) => ComputeHashes();

    private void OnUppercaseChanged(object sender, RoutedEventArgs e) => ComputeHashes();

    private void ComputeHashes()
    {
        var input = InputBox.Text;
        if (string.IsNullOrEmpty(input))
        {
            Md5Output.Text = Sha1Output.Text = Sha256Output.Text = Sha512Output.Text = string.Empty;
            return;
        }

        var md5 = HashService.ComputeMd5(input);
        var sha1 = HashService.ComputeSha1(input);
        var sha256 = HashService.ComputeSha256(input);
        var sha512 = HashService.ComputeSha512(input);

        if (ChkUppercase.IsChecked == true)
        {
            md5 = md5.ToUpperInvariant();
            sha1 = sha1.ToUpperInvariant();
            sha256 = sha256.ToUpperInvariant();
            sha512 = sha512.ToUpperInvariant();
        }

        Md5Output.Text = md5;
        Sha1Output.Text = sha1;
        Sha256Output.Text = sha256;
        Sha512Output.Text = sha512;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        InputBox.Text = string.Empty;
    }

    private void OnCopyMd5(object sender, RoutedEventArgs e) => CopyIfNotEmpty(Md5Output.Text);
    private void OnCopySha1(object sender, RoutedEventArgs e) => CopyIfNotEmpty(Sha1Output.Text);
    private void OnCopySha256(object sender, RoutedEventArgs e) => CopyIfNotEmpty(Sha256Output.Text);
    private void OnCopySha512(object sender, RoutedEventArgs e) => CopyIfNotEmpty(Sha512Output.Text);

    private static void CopyIfNotEmpty(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["input"] = InputBox.Text,
            ["uppercase"] = (ChkUppercase.IsChecked == true).ToString()
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("uppercase", out var upper))
            ChkUppercase.IsChecked = upper == "True";
        if (state.TryGetValue("input", out var input))
        {
            InputBox.Text = input;
            ComputeHashes();
        }
    }
}

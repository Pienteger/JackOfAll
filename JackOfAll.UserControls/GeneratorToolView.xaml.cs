using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class GeneratorToolView : UserControl, IStatefulTool
{
    public GeneratorToolView()
    {
        InitializeComponent();
        GeneratePassword();
        GenerateGuid();
    }

    // ── Password ────────────────────────────────────────────────────

    private void OnGeneratePassword(object sender, RoutedEventArgs e) => GeneratePassword();

    private void OnCopyPassword(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordOutput.Text))
            Clipboard.SetText(PasswordOutput.Text);
    }

    private void OnGeneratePasswordBatch(object sender, RoutedEventArgs e)
    {
        var (length, upper, lower, digits, symbols) = GetPasswordOptions();
        var lines = Enumerable.Range(0, 10)
            .Select(_ => GeneratorService.GeneratePassword(length, upper, lower, digits, symbols));
        PasswordBatchOutput.Text = string.Join(Environment.NewLine, lines);
        PasswordBatchOutput.Visibility = Visibility.Visible;
    }

    private void OnCopyPasswordBatch(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PasswordBatchOutput.Text))
            Clipboard.SetText(PasswordBatchOutput.Text);
    }

    private void GeneratePassword()
    {
        var (length, upper, lower, digits, symbols) = GetPasswordOptions();
        PasswordOutput.Text = GeneratorService.GeneratePassword(length, upper, lower, digits, symbols);
    }

    private (int length, bool upper, bool lower, bool digits, bool symbols) GetPasswordOptions()
    {
        if (!int.TryParse(PasswordLength.Text, out var length) || length < 1)
            length = 20;
        length = Math.Min(length, 256);

        return (length,
            ChkUpper.IsChecked == true,
            ChkLower.IsChecked == true,
            ChkDigits.IsChecked == true,
            ChkSymbols.IsChecked == true);
    }

    // ── GUID ────────────────────────────────────────────────────────

    private void OnGenerateGuid(object sender, RoutedEventArgs e) => GenerateGuid();

    private void OnCopyGuid(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(GuidOutput.Text))
            Clipboard.SetText(GuidOutput.Text);
    }

    private void OnGuidNoDashes(object sender, RoutedEventArgs e)
    {
        GuidOutput.Text = GeneratorService.GenerateGuidNoDashes();
    }

    private void OnGuidUppercase(object sender, RoutedEventArgs e)
    {
        GuidOutput.Text = GeneratorService.GenerateGuidUppercase();
    }

    private void OnGenerateGuidBatch(object sender, RoutedEventArgs e)
    {
        var lines = Enumerable.Range(0, 10).Select(_ => GeneratorService.GenerateGuid());
        GuidBatchOutput.Text = string.Join(Environment.NewLine, lines);
        GuidBatchOutput.Visibility = Visibility.Visible;
    }

    private void OnCopyGuidBatch(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(GuidBatchOutput.Text))
            Clipboard.SetText(GuidBatchOutput.Text);
    }

    private void GenerateGuid()
    {
        GuidOutput.Text = GeneratorService.GenerateGuid();
    }

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["length"] = PasswordLength.Text,
            ["upper"] = (ChkUpper.IsChecked == true).ToString(),
            ["lower"] = (ChkLower.IsChecked == true).ToString(),
            ["digits"] = (ChkDigits.IsChecked == true).ToString(),
            ["symbols"] = (ChkSymbols.IsChecked == true).ToString(),
            ["password"] = PasswordOutput.Text,
            ["guid"] = GuidOutput.Text
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("length", out var len))
            PasswordLength.Text = len;
        if (state.TryGetValue("upper", out var u))
            ChkUpper.IsChecked = u == "True";
        if (state.TryGetValue("lower", out var l))
            ChkLower.IsChecked = l == "True";
        if (state.TryGetValue("digits", out var d))
            ChkDigits.IsChecked = d == "True";
        if (state.TryGetValue("symbols", out var s))
            ChkSymbols.IsChecked = s == "True";
        if (state.TryGetValue("password", out var pw))
            PasswordOutput.Text = pw;
        if (state.TryGetValue("guid", out var g))
            GuidOutput.Text = g;
    }
}

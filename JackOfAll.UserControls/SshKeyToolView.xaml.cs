using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class SshKeyToolView : UserControl
{
    public SshKeyToolView()
    {
        InitializeComponent();
        LoadKeys();
    }

    private void LoadKeys()
    {
        var keys = SshKeyService.ListKeys();
        KeyGrid.ItemsSource = keys;
        PublicKeyPreview.Visibility = Visibility.Collapsed;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadKeys();

    private void OnKeySelected(object sender, SelectionChangedEventArgs e)
    {
        if (KeyGrid.SelectedItem is SshKeyInfo key)
        {
            PublicKeyPreview.Text = key.PublicKeyContent;
            PublicKeyPreview.Visibility = Visibility.Visible;
        }
    }

    private void OnCopyPublicKeyClick(object sender, RoutedEventArgs e)
    {
        if (KeyGrid.SelectedItem is SshKeyInfo key && !string.IsNullOrEmpty(key.PublicKeyContent))
            Clipboard.SetText(key.PublicKeyContent);
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var name = NewKeyName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowStatus("Please enter a key name.");
            return;
        }

        var type = (KeyType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ed25519";
        var comment = string.IsNullOrWhiteSpace(KeyComment.Text)
            ? Environment.UserName
            : KeyComment.Text.Trim();

        ShowStatus("Generating key...");
        var (success, output) = await SshKeyService.GenerateKeyAsync(name, type, comment);
        ShowStatus(output);

        if (success)
            LoadKeys();
    }

    private void ShowStatus(string message)
    {
        GenerateStatus.Text = message;
        GenerateStatus.Visibility = Visibility.Visible;
    }
}

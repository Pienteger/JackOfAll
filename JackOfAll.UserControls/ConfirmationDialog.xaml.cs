using System.Windows;

namespace JackOfAll.UserControls;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string heading, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
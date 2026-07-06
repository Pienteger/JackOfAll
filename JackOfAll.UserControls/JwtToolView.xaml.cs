using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class JwtToolView : UserControl, IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    public JwtToolView(IMessageBus bus, string? initialToken = null)
    {
        InitializeComponent();

        _subscription = bus.Subscribe<TokenDetectedMessage>(OnTokenDetected);

        if (initialToken is not null)
        {
            TokenInput.Text = initialToken;
            Decode(initialToken);
        }
    }

    private void OnTokenDetected(TokenDetectedMessage msg)
    {
        // Don't auto-populate if user already has input
    }

    private void OnDecodeClick(object sender, RoutedEventArgs e)
    {
        Decode(TokenInput.Text.Trim());
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        TokenInput.Clear();
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ShowError("Paste a JWT token above and click Decode.");
            return;
        }

        var result = JwtService.Decode(token);
        if (result is null)
        {
            ShowError("Invalid JWT token. Check the format and try again.");
            return;
        }

        ErrorPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;

        HeaderText.Text = result.Header;
        PayloadText.Text = result.Payload;
        SignatureText.Text = result.Signature;
        AlgorithmText.Text = result.Algorithm;
        IssuerText.Text = result.Issuer ?? "—";
        SubjectText.Text = result.Subject ?? "—";

        if (result.Expiry.HasValue)
        {
            if (result.IsExpired)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x1d, 0x1d));
                StatusIcon.Text = "✕";
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x9a, 0x9a));
                StatusText.Text = $"Expired {result.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x9a, 0x9a));
            }
            else
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x1d, 0x3a, 0x1d));
                StatusIcon.Text = "✓";
                StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0xc7, 0x84));
                StatusText.Text = $"Valid until {result.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0xc7, 0x84));
            }
        }
        else
        {
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x30));
            StatusIcon.Text = "—";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            StatusText.Text = "No expiry claim";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        }
    }

    private void ShowError(string message)
    {
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.Dispose();
    }
}

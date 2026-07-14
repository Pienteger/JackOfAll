using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class JwtToolView : UserControl, IDisposable, IStatefulTool
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
                StatusBorder.Background = FindBrush("ErrorBackground");
                StatusIcon.Text = "✕";
                StatusIcon.Foreground = FindBrush("ErrorText");
                StatusText.Text = $"Expired {result.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC";
                StatusText.Foreground = FindBrush("ErrorText");
            }
            else
            {
                StatusBorder.Background = FindBrush("SuccessBackground");
                StatusIcon.Text = "✓";
                StatusIcon.Foreground = FindBrush("SuccessText");
                StatusText.Text = $"Valid until {result.Expiry.Value:yyyy-MM-dd HH:mm:ss} UTC";
                StatusText.Foreground = FindBrush("SuccessText");
            }
        }
        else
        {
            StatusBorder.Background = FindBrush("SurfaceBackground");
            StatusIcon.Text = "—";
            StatusIcon.Foreground = FindBrush("TextSecondary");
            StatusText.Text = "No expiry claim";
            StatusText.Foreground = FindBrush("TextSecondary");
        }
    }

    private void ShowError(string message)
    {
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }

    private Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string> { ["token"] = TokenInput.Text };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("token", out var token) && !string.IsNullOrEmpty(token))
        {
            TokenInput.Text = token;
            Decode(token);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription.Dispose();
    }
}

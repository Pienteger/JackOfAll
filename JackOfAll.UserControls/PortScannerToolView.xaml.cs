using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class PortScannerToolView : UserControl, IStatefulTool
{
    private CancellationTokenSource? _cts;

    public PortScannerToolView()
    {
        InitializeComponent();
    }

    private async void OnListActiveClick(object sender, RoutedEventArgs e)
    {
        SetScanning(true);
        StatusText.Text = "Querying active listeners...";

        try
        {
            _cts = new CancellationTokenSource();
            var results = await PortScannerService.GetActiveTcpListenersAsync(_cts.Token);
            PortGrid.ItemsSource = results;
            StatusText.Text = $"{results.Count} listening ports found";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        finally
        {
            SetScanning(false);
        }
    }

    private async void OnScanCommonClick(object sender, RoutedEventArgs e)
    {
        SetScanning(true);
        StatusText.Text = "Scanning common ports...";

        try
        {
            _cts = new CancellationTokenSource();
            var results = await PortScannerService.ScanCommonPortsAsync(_cts.Token);
            PortGrid.ItemsSource = results;
            var open = results.Count(r => r.Status == "Open");
            StatusText.Text = $"Scan complete: {open} open, {results.Count - open} closed";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        finally
        {
            SetScanning(false);
        }
    }

    private async void OnScanRangeClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RangeStart.Text, out var start) || !int.TryParse(RangeEnd.Text, out var end)
            || start < 1 || end > 65535 || start > end)
        {
            StatusText.Text = "Invalid range. Use 1-65535.";
            return;
        }

        if (end - start > 10000)
        {
            StatusText.Text = "Range too large (max 10,000 ports).";
            return;
        }

        SetScanning(true);
        StatusText.Text = $"Scanning ports {start}-{end}...";

        try
        {
            _cts = new CancellationTokenSource();
            var results = await PortScannerService.ScanRangeAsync(start, end, _cts.Token);
            PortGrid.ItemsSource = results;
            StatusText.Text = $"Scan complete: {results.Count} open ports in range {start}-{end}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        finally
        {
            SetScanning(false);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private async void OnKillClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int pid || pid == 0) return;

        var confirm = MessageBox.Show(
            $"Kill process with PID {pid}?",
            "Confirm Kill",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var (success, message) = await Task.Run(() => PortScannerService.KillProcess(pid));
        StatusText.Text = message;

        if (success)
            OnListActiveClick(sender, e);
    }

    private void SetScanning(bool scanning)
    {
        BtnListActive.IsEnabled = !scanning;
        BtnScanCommon.IsEnabled = !scanning;
        BtnScanRange.IsEnabled = !scanning;
        BtnCancel.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
    }

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["rangeStart"] = RangeStart.Text,
            ["rangeEnd"] = RangeEnd.Text
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("rangeStart", out var start))
            RangeStart.Text = start;
        if (state.TryGetValue("rangeEnd", out var end))
            RangeEnd.Text = end;
    }
}

using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls.Connectivity;

/// <summary>"Who's on my network": a ping sweep of the local subnet plus the ARP table.</summary>
public partial class LanDevicesPanel : UserControl, IDisposable
{
    private CancellationTokenSource? _cts;
    private LanSubnet? _subnet;

    public LanDevicesPanel()
    {
        InitializeComponent();
    }

    public void Activate()
    {
        _subnet = LanScanService.FindSubnet();
        SubnetText.Text = _subnet is null
            ? "Not connected to a local network."
            : $"Subnet {_subnet.Display}  ·  this PC is {_subnet.Local}  ·  router {_subnet.Gateway}";
        BtnScan.IsEnabled = _subnet is not null && _cts is null;
    }

    public void Deactivate() { }

    public void Dispose() => _cts?.Cancel();

    public sealed record Row(string Ip, string Name, string Mac, string PingText, int PingSort, string Notes, uint SortKey);

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        Activate();
        if (_subnet is null) return;
        _cts = new CancellationTokenSource();
        BtnScan.IsEnabled = false;
        BtnCancel.Visibility = Visibility.Visible;
        ScanLabel.Text = "Scanning…";
        Progress.Fraction = 0;
        Progress.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        try
        {
            var devices = await LanScanService.ScanAsync(_subnet, new Progress<double>(f => Progress.Fraction = f), _cts.Token);
            Grid.ItemsSource = devices.Select(ToRow).ToList();
            var silent = devices.Count(d => d.PingMs < 0);
            SubnetText.Text = $"{devices.Count} device{(devices.Count == 1 ? "" : "s")} on {_subnet.Display}"
                + (silent > 0 ? $"  ·  {silent} ignore ping but answered ARP" : "")
                + $"  ·  scanned {DateTime.Now:t}";
            EmptyText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            SubnetText.Text = "Scan cancelled.";
            EmptyText.Visibility = Grid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _cts = null;
            BtnScan.IsEnabled = true;
            BtnCancel.Visibility = Visibility.Collapsed;
            ScanLabel.Text = "Scan again";
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private static Row ToRow(LanDevice d)
    {
        var notes = new List<string>();
        if (d.IsThisPc) notes.Add("This PC");
        if (d.IsRouter) notes.Add("Router");
        if (d.PrivateMac && !d.IsThisPc) notes.Add("Private MAC");
        if (d.PingMs < 0) notes.Add("Ignores ping");
        return new Row(d.Ip, d.Name, d.Mac,
            d.IsThisPc ? "" : d.PingMs < 0 ? "--" : $"{d.PingMs} ms",
            d.PingMs < 0 ? int.MaxValue : d.PingMs,
            string.Join(", ", notes), d.SortKey);
    }
}

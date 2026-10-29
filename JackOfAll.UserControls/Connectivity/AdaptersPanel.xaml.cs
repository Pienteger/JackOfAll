using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.UserControls.Helpers;

namespace JackOfAll.UserControls.Connectivity;

/// <summary>Every network adapter with its addresses and live traffic, plus the public IP on request.</summary>
public partial class AdaptersPanel : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ThroughputMeter _meter = new();
    private List<AdapterRow> _rows = [];
    private string _layoutKey = "";
    private CancellationTokenSource? _ipCts;

    public AdaptersPanel()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => OnTick();
    }

    public void Activate()
    {
        _timer.Start();
        OnTick();
    }

    public void Deactivate() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _ipCts?.Cancel();
    }

    private void OnTick()
    {
        var adapters = NetworkAdapterService.Snapshot();
        _meter.Sample(adapters);
        Chart.Down = _meter.DownHistory;
        Chart.Up = _meter.UpHistory;
        Chart.Redraw();
        DownRate.Text = NetworkAdapterService.FormatRate(_meter.DownBps);
        UpRate.Text = NetworkAdapterService.FormatRate(_meter.UpBps);

        var shown = adapters.Where(a => a.Up || ShowDown.IsChecked == true).ToList();
        AdaptersHeader.Text = $"ADAPTERS  ·  {adapters.Count(a => a.Up)} CONNECTED OF {adapters.Count}";

        // rebuild only when something other than traffic changed, so selected text survives the refresh
        var key = string.Join("|", shown.Select(a => $"{a.Id}{a.Up}{string.Join(",", a.IPv4)}{string.Join(",", a.Gateways)}{a.SpeedBps}"));
        if (key != _layoutKey)
        {
            _layoutKey = key;
            _rows = shown.Select(ToRow).ToList();
            AdapterList.ItemsSource = _rows;
        }
        foreach (var row in _rows)
            row.RateText = _meter.PerAdapter.TryGetValue(row.Id, out var r)
                ? $"↓ {NetworkAdapterService.FormatRate(r.Down)}   ↑ {NetworkAdapterService.FormatRate(r.Up)}"
                : "";
    }

    private void OnShowDownChanged(object sender, RoutedEventArgs e)
    {
        _layoutKey = "";
        OnTick();
    }

    public sealed record Field(string Key, string Value);

    public sealed class AdapterRow : INotifyPropertyChanged
    {
        private string _rateText = "";

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Glyph { get; init; } = "";
        public string Status { get; init; } = "";
        public Brush ChipBackground { get; init; } = Brushes.Transparent;
        public Brush ChipForeground { get; init; } = Brushes.Gray;
        public double Opacity { get; init; } = 1;
        public IReadOnlyList<Field> Fields { get; init; } = [];

        public string RateText
        {
            get => _rateText;
            set
            {
                if (_rateText == value) return;
                _rateText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RateText)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static AdapterRow ToRow(NetworkAdapter a)
    {
        var fields = new List<Field>();
        void Add(string k, IEnumerable<string> v)
        {
            var s = string.Join("\n", v);
            if (s.Length > 0) fields.Add(new Field(k, s));
        }
        Add("IPv4", a.IPv4);
        Add("IPv6", a.IPv6.Take(2));
        Add("Gateway", a.Gateways);
        Add("DNS", a.Dns.Take(3));
        Add("MAC", [a.Mac]);
        Add("Link speed", [NetworkAdapterService.FormatSpeed(a.SpeedBps)]);
        if (a.Up && a.IPv4.Count > 0) Add("Address from", [a.Dhcp ? "DHCP (automatic)" : "Set manually"]);

        var (status, color) = !a.Up ? ("Disconnected", NetPalette.Secondary) : a.IsDefaultRoute ? ("Internet", NetPalette.Good) : ("Connected", NetPalette.Accent);
        return new AdapterRow
        {
            Id = a.Id,
            Name = a.Name,
            Description = $"{a.Kind}  ·  {a.Description}",
            Glyph = a.Kind switch
            {
                "Wi-Fi" => "",            // Wifi
                "Ethernet" => "",         // Ethernet
                "Bluetooth" => "",        // Bluetooth
                "VPN" => "",              // VPN
                "Mobile broadband" => "", // Cellular
                _ => "",                  // Network
            },
            Status = status,
            ChipBackground = NetPalette.Brush(NetPalette.WithAlpha(color, 40)),
            ChipForeground = NetPalette.Brush(color),
            Opacity = a.Up ? 1 : 0.6,
            Fields = fields,
        };
    }

    private async void OnPublicIpClick(object sender, RoutedEventArgs e)
    {
        _ipCts?.Cancel();
        _ipCts = new CancellationTokenSource();
        BtnPublicIp.IsEnabled = false;
        PublicIp.Text = "Asking…";
        try
        {
            var info = await NetworkAdapterService.GetPublicIpAsync(_ipCts.Token);
            PublicIp.Text = info.Ip + (info.Country.Length > 0 ? $"   ({info.Country}, via Cloudflare {info.Datacenter})" : "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            PublicIp.Text = "Couldn't reach Cloudflare.";
        }
        finally
        {
            BtnPublicIp.IsEnabled = true;
        }
    }
}

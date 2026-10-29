using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.UserControls.Helpers;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls.Connectivity;

/// <summary>
/// Bluetooth radio state, paired devices (connection and battery), a live scan of nearby LE
/// devices, and a hot/cold finder for tracking one of them down.
/// </summary>
public partial class BluetoothPanel : UserControl, IDisposable
{
    private static readonly TimeSpan PairedRefresh = TimeSpan.FromSeconds(10);

    private readonly BluetoothService _bt = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<NearbyRow> _nearby = [];
    private DateTime _pairedAt = DateTime.MinValue;
    private bool _active, _paused, _refreshing;
    private ulong? _target;
    private string _targetName = "";
    private SignalBeeper? _beeper;

    public BluetoothPanel()
    {
        InitializeComponent();
        NearbyList.ItemsSource = _nearby;
        _timer.Tick += (_, _) => OnTick();
        ShowFinder();
    }

    /// <summary>Shared with the Wi-Fi hunt so one Beeps switch covers both.</summary>
    public SignalBeeper? Beeper
    {
        get => _beeper;
        set { _beeper = value; UpdateBeepsButton(); }
    }

    /// <summary>The finder target's signal, or NaN when nothing is targeted or it went quiet.</summary>
    public double FinderDbm
    {
        get
        {
            var d = _target is { } t ? _bt.Find(t) : null;
            return d is not null && DateTime.Now - d.LastSeen < TimeSpan.FromSeconds(5) ? d.SmoothedRssi : double.NaN;
        }
    }

    public void Activate()
    {
        _active = true;
        if (!_paused) _bt.StartScan();
        _timer.Start();
        UpdateBeepsButton();
        OnTick();
    }

    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
        _bt.StopScan(); // scanning costs battery, so only while the tab is open
    }

    public void Dispose()
    {
        _timer.Stop();
        _bt.Dispose();
    }

    private void OnTick()
    {
        if (DateTime.Now - _pairedAt > PairedRefresh) _ = RefreshPairedAsync();
        UpdateNearby();
        ShowFinder();
    }

    // ---------------------------------------------------------------- radio and paired devices

    private sealed record PairedRow(string Name, string Category, string Glyph, Brush GlyphBrush, string Status,
        Brush ChipBackground, Brush ChipForeground, Visibility BatteryVisibility, double BatteryFraction, Brush BatteryBrush, string BatteryText);

    private async Task RefreshPairedAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        _pairedAt = DateTime.Now;
        try
        {
            var radio = await BluetoothService.GetRadioAsync();
            ShowRadio(radio);
            List<PairedBluetoothDevice> paired = radio.State == BluetoothRadioState.NoAdapter ? [] : await BluetoothService.GetPairedAsync();
            if (!_active) return;
            PairedList.ItemsSource = paired.Select(ToRow).ToList();
            PairedHeader.Text = $"PAIRED DEVICES  ·  {paired.Count(p => p.Connected)} CONNECTED OF {paired.Count}";
            PairedEmpty.Text = radio.State == BluetoothRadioState.NoAdapter ? "This PC has no Bluetooth adapter." : "No paired Bluetooth devices.";
            PairedEmpty.Visibility = paired.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowRadio(BluetoothRadioInfo r)
    {
        var (text, color) = r.State switch
        {
            BluetoothRadioState.On => ("Bluetooth is on", NetPalette.Good),
            BluetoothRadioState.Off => ("Bluetooth is off", NetPalette.Okay),
            BluetoothRadioState.NoAdapter => ("No Bluetooth adapter", NetPalette.Bad),
            _ => ("Bluetooth status unknown", NetPalette.Secondary),
        };
        RadioText.Text = text;
        RadioDot.Fill = NetPalette.Brush(color);
        RadioDetail.Text = r.State == BluetoothRadioState.NoAdapter ? ""
            : $"{(r.LowEnergy ? "Supports Bluetooth LE" : "No Bluetooth LE")}{(r.Address.Length > 0 ? "  ·  " + r.Address : "")}";
        if (r.State == BluetoothRadioState.Off) RadioDetail.Text = "Turn it on in Settings > Bluetooth & devices to scan.";
    }

    private static PairedRow ToRow(PairedBluetoothDevice d)
    {
        var glyph = d.Category switch
        {
            "Audio" => "",        // Headphone
            "Input device" => "", // Keyboard
            "Phone" => "",        // CellPhone
            "Computer" => "",     // DeviceLaptopNoPic
            _ => "",              // Bluetooth
        };
        var chip = d.Connected ? NetPalette.Good : NetPalette.Secondary;
        var level = d.BatteryPercent ?? 0;
        var batteryColor = level <= 15 ? NetPalette.Bad : level <= 35 ? NetPalette.Okay : NetPalette.Good;
        return new PairedRow(d.Name, d.Category, glyph, NetPalette.Brush(d.Connected ? NetPalette.Accent : NetPalette.Dimmed),
            d.Connected ? "Connected" : "Not connected",
            NetPalette.Brush(NetPalette.WithAlpha(chip, 40)), NetPalette.Brush(chip),
            d.BatteryPercent.HasValue ? Visibility.Visible : Visibility.Collapsed,
            level / 100.0, NetPalette.Brush(batteryColor), $"{level}%");
    }

    // ---------------------------------------------------------------- nearby scan

    /// <summary>One nearby device; updated in place so the list doesn't flicker or eat clicks.</summary>
    public sealed class NearbyRow : INotifyPropertyChanged
    {
        private string _name = "", _detail = "", _rssiText = "", _distanceText = "";
        private double _fraction;
        private Brush _barBrush = Brushes.Gray, _dotBrush = Brushes.Gray;
        private FontWeight _weight = FontWeights.Normal;

        public ulong Address { get; init; }
        public double Rssi { get; set; }
        public string Name { get => _name; set => Set(ref _name, value); }
        public string Detail { get => _detail; set => Set(ref _detail, value); }
        public string RssiText { get => _rssiText; set => Set(ref _rssiText, value); }
        public string DistanceText { get => _distanceText; set => Set(ref _distanceText, value); }
        public double Fraction { get => _fraction; set => Set(ref _fraction, value); }
        public Brush BarBrush { get => _barBrush; set => Set(ref _barBrush, value); }
        public Brush DotBrush { get => _dotBrush; set => Set(ref _dotBrush, value); }
        public FontWeight Weight { get => _weight; set => Set(ref _weight, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void UpdateNearby()
    {
        var devices = _bt.Nearby();
        var live = devices.Select(d => d.Address).ToHashSet();
        for (var i = _nearby.Count - 1; i >= 0; i--)
            if (!live.Contains(_nearby[i].Address)) _nearby.RemoveAt(i);

        foreach (var d in devices)
        {
            var row = _nearby.FirstOrDefault(r => r.Address == d.Address);
            if (row is null) _nearby.Add(row = new NearbyRow { Address = d.Address });
            var targeted = _target == d.Address;
            row.Rssi = d.SmoothedRssi;
            row.Name = d.Shown + (targeted ? "  (finding)" : "");
            row.Detail = (d.Maker.Length > 0 && d.Name.Length > 0 ? d.Maker + "  ·  " : "") + d.AddressText;
            row.RssiText = $"{Math.Round(d.SmoothedRssi)}";
            row.DistanceText = FormatDistance(d.EstimatedMeters);
            row.Fraction = NetPalette.Fraction(d.SmoothedRssi);
            row.BarBrush = NetPalette.Brush(NetPalette.Signal(d.SmoothedRssi));
            row.DotBrush = NetPalette.Brush(targeted ? NetPalette.Accent : NetPalette.Hue(d.Shown));
            row.Weight = targeted || d.Name.Length > 0 ? FontWeights.SemiBold : FontWeights.Normal;
        }

        // keep strongest first; Move keeps the row objects (and any button mid-click) alive
        var order = _nearby.OrderByDescending(r => r.Rssi).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var at = _nearby.IndexOf(order[i]);
            if (at != i) _nearby.Move(at, i);
        }

        NearbyCount.Text = devices.Count == 0 ? "" : $"{devices.Count} heard in the last 30 s";
        NearbyEmpty.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NearbyEmpty.Text = _bt.ScanError.Length > 0 ? _bt.ScanError : _paused ? "Scan paused." : "Listening…";
        ScanLabel.Text = _paused ? "Resume scan" : "Pause scan";
    }

    private static string FormatDistance(double m) => double.IsNaN(m) ? "" : m < 1 ? "< 1 m" : m < 10 ? $"≈ {m:0.0} m" : $"≈ {m:0} m";

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused) _bt.StopScan();
        else if (_active) _bt.StartScan();
        UpdateNearby();
    }

    // ---------------------------------------------------------------- finder

    private void OnFindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ulong address }) return;
        _target = _target == address ? null : address;
        _targetName = _bt.Find(address)?.Shown ?? "";
        UpdateNearby();
        ShowFinder();
    }

    private void ShowFinder()
    {
        var device = _target is { } t ? _bt.Find(t) : null;
        var dbm = FinderDbm;
        FinderGauge.Dbm = dbm;
        FinderGauge.Best = double.NaN;
        FinderGauge.Redraw();
        FinderSpark.Samples = device?.History() ?? [];
        FinderSpark.Redraw();
        BtnBeeps.Visibility = _target is null ? Visibility.Hidden : Visibility.Visible;

        if (_target is null)
        {
            FinderName.Text = "Pick a device to find";
            FinderDistance.Text = "Press Find next to anything in the nearby list.";
            FinderTrend.Text = "Then walk around with this PC.";
            FinderTrend.Foreground = NetPalette.Brush(NetPalette.Secondary);
            return;
        }
        FinderName.Text = device?.Shown ?? _targetName;
        if (double.IsNaN(dbm))
        {
            FinderDistance.Text = "Not heard for a few seconds";
            FinderTrend.Text = "Out of range, switched off, or it changed its address.";
            FinderTrend.Foreground = NetPalette.Brush(NetPalette.Okay);
            return;
        }
        FinderDistance.Text = $"Roughly {FormatDistance(device!.EstimatedMeters).TrimStart('≈', ' ')} away";
        var history = device.History();
        var (trend, color) = NetPalette.Trend(history[^Math.Min(20, history.Length)..]);
        FinderTrend.Text = dbm >= -45 ? "Right here! Look around you." : trend;
        FinderTrend.Foreground = NetPalette.Brush(dbm >= -45 ? NetPalette.Good : color);
    }

    private void OnBeepsClick(object sender, RoutedEventArgs e)
    {
        if (_beeper is null) return;
        _beeper.Enabled = !_beeper.Enabled;
        UpdateBeepsButton();
    }

    public void UpdateBeepsButton()
    {
        var on = _beeper?.Enabled == true;
        BeepsLabel.Text = on ? "Beeps on" : "Beeps off";
        BeepsGlyph.Text = on ? "" : "";
    }
}

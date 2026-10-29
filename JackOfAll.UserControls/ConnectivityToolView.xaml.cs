using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Helpers;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls;

/// <summary>
/// Wi-Fi companion: hunt for the best spot by signal (with metal-detector beeps), see channel
/// crowding, tell Wi-Fi problems apart from ISP problems, and share a saved network as a QR code.
/// </summary>
public partial class ConnectivityToolView : UserControl, IStatefulTool, IDisposable
{
    private const int HuntTab = 0, AirspaceTab = 1, PulseTab = 2, ShareTab = 3, BluetoothTab = 4, AdaptersTab = 5, LanTab = 6;
    private const double MeasureSeconds = 3;

    private readonly WifiMonitor _wifi = new();
    private readonly PingMonitor _ping = new();
    private readonly SignalBeeper _beeper = new();
    private readonly SpeedTestService _speed = new();
    private readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _slow = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly List<WifiSpot> _spots = WifiSpotStore.Load();
    private CancellationTokenSource? _speedCts;

    private double _shown = double.NaN, _best = double.NaN;
    private string _bestSsid = "", _spotsSsid = "\0";
    private string? _measuringName;
    private long _measureStart;
    private DateTime _measureEnd;

    private List<SavedWifiNetwork>? _saved;
    private string _nearbyKey = "", _adviceKey = "", _readinessKey = "";

    public ConnectivityToolView()
    {
        InitializeComponent();
        _wifi.Start();
        _ping.Start();
        _fast.Tick += (_, _) => OnFastTick();
        _slow.Tick += (_, _) => OnSlowTick();
        _fast.Start();
        _slow.Start();
        PreviewKeyDown += OnPreviewKeyDown;
        Bluetooth.Beeper = _beeper;
        UpdateBeepsButton();
        OnSlowTick();
    }

    public void Dispose()
    {
        _fast.Stop();
        _slow.Stop();
        _speedCts?.Cancel();
        _beeper.Dispose();
        Bluetooth.Dispose();
        Adapters.Dispose();
        LanDevices.Dispose();
        _ping.Dispose();
        _wifi.Dispose();
    }

    // ---------------------------------------------------------------- timers

    private void OnFastTick()
    {
        var link = _wifi.Link;
        if (link.Connected)
        {
            if (double.IsNaN(_shown)) _shown = link.Rssi;
            _shown += (link.Rssi - _shown) * 0.3;
            if (_bestSsid != link.Ssid) { _bestSsid = link.Ssid; _best = double.NaN; }
            var recent = _wifi.History(3);
            if (recent.Length == 3)
            {
                var avg = recent.Average();
                if (double.IsNaN(_best) || avg > _best) _best = avg;
            }
        }
        else
        {
            _shown = double.NaN;
        }
        // one beeper: it follows the Bluetooth finder on that tab, the Wi-Fi signal everywhere else
        var finder = Bluetooth.FinderDbm;
        _beeper.Dbm = Sections.SelectedIndex == BluetoothTab ? (double.IsNaN(finder) ? -100 : (float)finder)
            : link.Connected ? link.Rssi : -100;

        if (_measuringName is not null && DateTime.Now >= _measureEnd) FinishMeasure();
        if (Sections.SelectedIndex == HuntTab) UpdateHunt(link);
    }

    private void OnSlowTick()
    {
        var link = _wifi.Link;
        LinkSummary.Text = !_wifi.Client.Ok ? _wifi.Client.Error
            : !link.Connected ? "Not connected to Wi-Fi."
            : $"{link.Ssid}  ·  {(link.Band.Length > 0 ? link.Band : "?")}, channel {link.Channel}  ·  {link.Phy}  ·  {link.RxMbps} Mbps link  ·  {link.Auth}";
        LinkSummary.Foreground = (Brush)FindResource(link.Connected ? "TextSecondary" : "WarningBrush");

        if (_spotsSsid != link.Ssid) RefreshSpots();
        switch (Sections.SelectedIndex)
        {
            case AirspaceTab: UpdateAirspace(link); break;
            case PulseTab: UpdatePulse(link); break;
        }
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Sections)) return; // ignore bubbling from the saved-networks list
        if (!IsInitialized) return; // first selection happens while XAML is still loading, before the panels exist
        var tab = Sections.SelectedIndex;
        if (tab == AirspaceTab && (DateTime.Now - _wifi.LastScan).TotalSeconds > 10) _wifi.ScanNow();
        _wifi.AutoScan = tab == AirspaceTab;
        if (tab != HuntTab) CancelNaming();
        if (tab == ShareTab) EnsureSaved();
        if (tab == HuntTab) UpdateBeepsButton();
        // the panels poll (and Bluetooth scans) only while their tab is open
        if (tab == BluetoothTab) Bluetooth.Activate(); else Bluetooth.Deactivate();
        if (tab == AdaptersTab) Adapters.Activate(); else Adapters.Deactivate();
        if (tab == LanTab) LanDevices.Activate(); else LanDevices.Deactivate();
        OnSlowTick();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.B when Sections.SelectedIndex == HuntTab: OnBeepsClick(this, e); break;
            case Key.M when Sections.SelectedIndex == HuntTab: BeginNaming(); break;
            case Key.R when Sections.SelectedIndex == AirspaceTab: OnRescanClick(this, e); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- Hunt

    private void UpdateHunt(WifiLink link)
    {
        var has = link.Connected && !double.IsNaN(_shown);
        Gauge.Dbm = has ? _shown : double.NaN;
        Gauge.Best = _best;
        Gauge.Redraw();
        Sparkline.Samples = _wifi.History(240);
        Sparkline.Redraw();
        BtnBeeps.IsEnabled = has;
        BtnMark.IsEnabled = has;
        BtnResetBest.Visibility = has && !double.IsNaN(_best) ? Visibility.Visible : Visibility.Hidden;

        if (!has)
        {
            QualityLabel.Text = "";
            QualityQuip.Text = _wifi.Client.Ok ? "Connect to a Wi-Fi network to start hunting." : _wifi.Client.Error;
            TrendText.Text = "Waiting for a signal";
            HuntStats.Text = "";
            return;
        }

        var (label, quip) = WifiAnalysisService.Describe(WifiAnalysisService.Level(_shown));
        QualityLabel.Text = label;
        QualityLabel.Foreground = NetPalette.Brush(NetPalette.Signal(_shown));
        QualityQuip.Text = quip;

        var (trend, trendColor) = NetPalette.Trend(_wifi.History(20));
        TrendText.Text = trend;
        TrendText.Foreground = NetPalette.Brush(trendColor);
        HuntStats.Text = $"Signal {link.Quality}%   ·   Link {link.RxMbps} Mbps   ·   Best here {(double.IsNaN(_best) ? "--" : Math.Round(_best).ToString())} dBm";

        if (_measuringName is not null)
            MeasureBar.Fraction = 1 - (_measureEnd - DateTime.Now).TotalSeconds / MeasureSeconds;
    }

    private void OnBeepsClick(object sender, RoutedEventArgs e)
    {
        _beeper.Enabled = !_beeper.Enabled;
        UpdateBeepsButton();
    }

    private void UpdateBeepsButton()
    {
        BeepsLabel.Text = _beeper.Enabled ? "Beeps on" : "Beeps off";
        BeepsGlyph.Text = _beeper.Enabled ? "" : ""; // Volume / Mute
    }

    private void OnResetBestClick(object sender, RoutedEventArgs e) => _best = double.NaN;

    private void OnMarkClick(object sender, RoutedEventArgs e) => BeginNaming();

    private void BeginNaming()
    {
        if (!_wifi.Link.Connected || _measuringName is not null) return;
        SpotName.Text = "";
        BtnMark.Visibility = Visibility.Collapsed;
        NamePanel.Visibility = Visibility.Visible;
        SpotName.Focus();
    }

    private void CancelNaming()
    {
        NamePanel.Visibility = Visibility.Collapsed;
        if (_measuringName is null) BtnMark.Visibility = Visibility.Visible;
    }

    private void OnSpotNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { StartMeasure(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelNaming(); e.Handled = true; }
    }

    private void OnMeasureClick(object sender, RoutedEventArgs e) => StartMeasure();

    private void StartMeasure()
    {
        var ssid = _wifi.Link.Ssid;
        var name = SpotName.Text.Trim();
        if (name.Length == 0) name = $"Spot {_spots.Count(s => s.Ssid == ssid) + 1}";
        _measuringName = name;
        _measureStart = _wifi.SampleCount;
        _measureEnd = DateTime.Now.AddSeconds(MeasureSeconds);
        NamePanel.Visibility = Visibility.Collapsed;
        MeasurePanel.Visibility = Visibility.Visible;
        MeasureText.Text = $"Measuring “{name}”… hold still";
        MeasureBar.Fraction = 0;
    }

    private void FinishMeasure()
    {
        var link = _wifi.Link;
        var got = (int)Math.Min(240, _wifi.SampleCount - _measureStart);
        var samples = _wifi.History(got);
        if (samples.Length > 0 && link.Connected)
        {
            var name = _measuringName!;
            _spots.RemoveAll(s => s.Ssid == link.Ssid && s.Name == name);
            _spots.Add(new WifiSpot(link.Ssid, name, (int)Math.Round(samples.Average())));
            WifiSpotStore.Save(_spots);
        }
        _measuringName = null;
        MeasurePanel.Visibility = Visibility.Collapsed;
        BtnMark.Visibility = Visibility.Visible;
        RefreshSpots();
    }

    private void OnDeleteSpotClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WifiSpot spot }) return;
        _spots.Remove(spot);
        WifiSpotStore.Save(_spots);
        RefreshSpots();
    }

    private sealed record SpotRow(int Rank, string Name, string DbmText, string Quality, double Fraction,
        Brush Brush, Brush BadgeBrush, Brush BadgeForeground, WifiSpot Spot);

    private static readonly Color[] Medals = [Color.FromRgb(242, 200, 90), Color.FromRgb(200, 208, 224), Color.FromRgb(214, 150, 100)];

    private void RefreshSpots()
    {
        var ssid = _wifi.Link.Ssid;
        _spotsSsid = ssid;
        var mine = _spots.Where(s => s.Ssid == ssid).OrderByDescending(s => s.Dbm).ToList();
        SpotsList.ItemsSource = mine.Select((s, i) => new SpotRow(
            i + 1, s.Name, $"{s.Dbm} dBm", WifiAnalysisService.Describe(WifiAnalysisService.Level(s.Dbm)).Label,
            NetPalette.Fraction(s.Dbm), NetPalette.Brush(NetPalette.Signal(s.Dbm)),
            i < 3 ? NetPalette.Brush(Medals[i]) : NetPalette.Brush(NetPalette.Track),
            i < 3 ? NetPalette.Brush(Color.FromRgb(30, 30, 40)) : NetPalette.Brush(NetPalette.Text),
            s)).ToList();
        SpotsEmpty.Visibility = mine.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (mine.Count < 2)
        {
            SpotsTip.Text = "";
            return;
        }
        var (best, worst) = (mine[0], mine[^1]);
        var diff = best.Dbm - worst.Dbm;
        SpotsTip.Text = diff >= 15
            ? $"“{best.Name}” gets about {Math.Round(Math.Pow(10, diff / 10.0))}x the signal power of “{worst.Name}”."
            : $"Your spots are within {diff} dB of each other. Pretty even coverage.";
    }

    // ---------------------------------------------------------------- Airspace

    private sealed record NetworkRow(string Name, FontWeight Weight, string BandChannel, int Rssi, double Fraction, Brush BarBrush, Brush DotBrush);

    private void UpdateAirspace(WifiLink link)
    {
        var nets = _wifi.Networks;
        var on24 = nets.Where(b => b.FreqMHz < 3000).ToList();
        var on5 = nets.Where(b => b.FreqMHz is >= 3000 and <= 5925).ToList();
        Configure(Spectrum24, on24, 2401, 2495, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13], dfs: false);
        Configure(Spectrum5, on5, 5160, 5895, [36, 44, 52, 60, 100, 108, 116, 124, 132, 140, 149, 157, 165, 173], dfs: true);
        Count24.Text = Plural(on24.Count, "network");
        Count5.Text = Plural(on5.Count, "network");

        var report = WifiAnalysisService.Analyze(link, nets, _ping.Router.Target);
        CrowdChip.Visibility = report.Crowd == ChannelCrowd.Unknown ? Visibility.Collapsed : Visibility.Visible;
        var crowdColor = report.Crowd switch
        {
            ChannelCrowd.Empty or ChannelCrowd.Quiet => NetPalette.Good,
            ChannelCrowd.Busy => NetPalette.Okay,
            _ => NetPalette.Bad,
        };
        CrowdText.Text = report.Crowd.ToString();
        CrowdText.Foreground = NetPalette.Brush(crowdColor);
        CrowdChip.Background = NetPalette.Brush(NetPalette.WithAlpha(crowdColor, 45));

        var adviceKey = string.Join("\n", report.Advice);
        if (adviceKey != _adviceKey)
        {
            _adviceKey = adviceKey;
            AdviceList.ItemsSource = report.Advice;
        }
        ScoreboardPanel.Visibility = report.Scoreboard.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Scoreboard.Scores = report.Scoreboard;
        Scoreboard.Redraw();

        NearbyHeader.Text = $"NEARBY  ·  {report.Count24} ON 2.4  ·  {report.Count5} ON 5  ·  {report.Count6} ON 6 GHZ";
        var scanning = (DateTime.Now - _wifi.LastScan).TotalSeconds < 4;
        RescanLabel.Text = scanning ? "Scanning…" : "Rescan";
        BtnRescan.IsEnabled = !scanning && _wifi.Client.Ok;

        var nearbyKey = string.Join("|", nets.Select(b => $"{b.Bssid}{b.Rssi}{b.Current}"));
        if (nearbyKey != _nearbyKey)
        {
            _nearbyKey = nearbyKey;
            NearbyList.ItemsSource = nets.Select(b => new NetworkRow(
                b.Shown + (b.Current ? "  (you)" : b.Mine ? "  (your router)" : ""),
                b.Mine ? FontWeights.SemiBold : FontWeights.Normal,
                $"{(b.FreqMHz < 3000 ? "2.4" : b.FreqMHz <= 5925 ? "5" : "6")} ch{b.Channel}",
                b.Rssi, NetPalette.Fraction(b.Rssi), NetPalette.Brush(NetPalette.Signal(b.Rssi)),
                NetPalette.Brush(b.Mine ? NetPalette.Accent : NetPalette.Hue(b.Ssid)))).ToList();
        }
        NearbyEmpty.Text = _wifi.Client.Ok ? "Looking around…" : _wifi.Client.Error;
        NearbyEmpty.Visibility = nets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Configure(SpectrumChart chart, IReadOnlyList<WifiBss> nets, int lo, int hi, int[] grid, bool dfs)
    {
        chart.Networks = nets;
        chart.FreqLo = lo;
        chart.FreqHi = hi;
        chart.GridChannels = grid;
        chart.ShowDfs = dfs;
        chart.Redraw();
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private void OnRescanClick(object sender, RoutedEventArgs e)
    {
        _wifi.ScanNow();
        RescanLabel.Text = "Scanning…";
        BtnRescan.IsEnabled = false;
    }

    // ---------------------------------------------------------------- Pulse

    private void UpdatePulse(WifiLink link)
    {
        var router = _ping.Router.Last(PingSeries.Capacity);
        var internet = _ping.Internet.Last(PingSeries.Capacity);
        Latency.Router = router;
        Latency.Internet = internet;
        Latency.Redraw();
        RouterLegend.Text = _ping.Router.Target.Length > 0 ? $"Router ({_ping.Router.Target})" : "Router";

        // Pings taken while the speed test fills the pipe, or while a Wi-Fi scan has the radio off-channel,
        // are high because of this tool itself, so judge only the samples after both (2 samples a second, 30 s max).
        var sinceSpeed = (DateTime.Now - _speed.FinishedAt).TotalSeconds;
        var sinceScan = (DateTime.Now - _wifi.LastScan).TotalSeconds - 4; // a scan takes a few seconds to finish
        var window = _speed.Running ? 0 : (int)Math.Clamp(Math.Min(sinceSpeed, sinceScan) * 2, 0, 60);
        var rs = PingStats.Of(router[Math.Max(0, router.Length - window)..]);
        var ist = PingStats.Of(internet[Math.Max(0, internet.Length - window)..]);
        ShowPingCard(rs, RouterAvg, RouterJitter, RouterLoss, _ping.Router.Target.Length == 0 ? "No router found" : null);
        ShowPingCard(ist, InternetAvg, InternetJitter, InternetLoss, null);

        var verdict = PingMonitor.Judge(link.Connected, _speed.Running, rs, ist,
            _speed.Phase == SpeedTestPhase.Done ? _speed.DownMbps : null);
        VerdictHead.Text = verdict.Headline;
        VerdictHead.Foreground = NetPalette.Brush(verdict.Kind switch
        {
            PulseVerdictKind.AllClear => NetPalette.Good,
            PulseVerdictKind.WifiProblem or PulseVerdictKind.InternetDown => NetPalette.Bad,
            PulseVerdictKind.IspProblem or PulseVerdictKind.NotConnected => NetPalette.Okay,
            PulseVerdictKind.UnderLoad => NetPalette.Router,
            _ => NetPalette.Secondary,
        });
        VerdictBody.Text = verdict.Detail;
        ShowReadiness(verdict);
        UpdateSpeed(link);
    }

    private static void ShowPingCard(PingStats s, TextBlock avg, TextBlock jitter, TextBlock loss, string? missing)
    {
        var dim = NetPalette.Brush(NetPalette.Secondary);
        if (missing is not null || s.Samples == 0)
        {
            avg.Text = "--";
            jitter.Text = missing ?? "Measuring…";
            jitter.Foreground = dim;
            loss.Text = "";
            return;
        }
        avg.Text = s.AllLost ? "--" : $"{s.AvgMs:0}";
        jitter.Text = $"Jitter  {s.JitterMs:0.0} ms";
        jitter.Foreground = s.JitterMs > 30 ? NetPalette.Brush(NetPalette.Bad) : s.JitterMs > 10 ? NetPalette.Brush(NetPalette.Okay) : dim;
        loss.Text = $"Loss  {s.LossPercent:0.#}%";
        loss.Foreground = s.LossPercent >= 5 ? NetPalette.Brush(NetPalette.Bad) : s.LossPercent > 0 ? NetPalette.Brush(NetPalette.Okay) : dim;
    }

    private void ShowReadiness(PulseVerdict v)
    {
        (string Name, bool Ok)[] items = v.HasReadiness
            ? [("Browsing", v.Browsing), ("Video calls", v.VideoCalls), ("Gaming", v.Gaming), ("4K streaming", v.Streaming4K)]
            : [];
        var key = string.Join(",", items.Select(i => $"{i.Name}{i.Ok}"));
        if (key == _readinessKey) return;
        _readinessKey = key;
        ReadinessPanel.Children.Clear();
        foreach (var (name, ok) in items)
        {
            var c = ok ? NetPalette.Good : NetPalette.Bad;
            ReadinessPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 6, 4),
                Background = NetPalette.Brush(NetPalette.WithAlpha(c, 45)),
                Child = new TextBlock { Text = (ok ? "✓ " : "✗ ") + name, FontSize = 11, Foreground = NetPalette.Brush(c) },
            });
        }
    }

    private async void OnSpeedTestClick(object sender, RoutedEventArgs e)
    {
        if (_speed.Running) return;
        _speedCts = new CancellationTokenSource();
        var run = _speed.RunAsync(_speedCts.Token);
        UpdateSpeed(_wifi.Link);
        await run;
        UpdateSpeed(_wifi.Link);
    }

    private void UpdateSpeed(WifiLink link)
    {
        var phase = _speed.Phase;
        BtnSpeed.IsEnabled = !_speed.Running && link.Connected;
        SpeedButtonLabel.Text = _speed.Running ? "Testing…" : phase is SpeedTestPhase.Done or SpeedTestPhase.Error ? "Run again" : "Run speed test";

        ShowSpeedNumber(DownLabel, DownValue, "DOWNLOAD", phase == SpeedTestPhase.Download ? _speed.LiveMbps : _speed.DownMbps,
            live: phase == SpeedTestPhase.Download, done: phase is SpeedTestPhase.Upload or SpeedTestPhase.Done);
        ShowSpeedNumber(UpLabel, UpValue, "UPLOAD", phase == SpeedTestPhase.Upload ? _speed.LiveMbps : _speed.UpMbps,
            live: phase == SpeedTestPhase.Upload, done: phase == SpeedTestPhase.Done);

        SpeedNote.Foreground = (Brush)FindResource(phase == SpeedTestPhase.Error ? "DangerBrush" : "TextSecondary");
        if (phase == SpeedTestPhase.Error)
            SpeedNote.Text = "The test couldn't reach Cloudflare: " + _speed.Error;
        else if (_speed.Running)
            SpeedNote.Text = "Hold tight…";
        else if (phase == SpeedTestPhase.Done && link is { Connected: true, RxMbps: > 0 })
        {
            // real-world Wi-Fi throughput is typically about 60% of the negotiated link rate
            var wifiCap = link.RxMbps * 0.6;
            SpeedNote.Text = _speed.DownMbps >= wifiCap * 0.8
                ? $"Your Wi-Fi link ({link.RxMbps} Mbps, so about {wifiCap:0} Mbps in practice) is the bottleneck. Get closer or use 5 GHz to go faster."
                : $"Wi-Fi can carry about {wifiCap:0} Mbps here, more than you got, so the limit is your internet plan or ISP, not the Wi-Fi.";
        }
    }

    private void ShowSpeedNumber(TextBlock label, TextBlock value, string name, double mbps, bool live, bool done)
    {
        label.Text = name + (live ? "  ●" : "");
        label.Foreground = live ? NetPalette.Brush(NetPalette.Router) : (Brush)FindResource("TextSecondary");
        value.Text = live || done ? (mbps >= 100 ? $"{mbps:0}" : $"{mbps:0.0}") : "--";
        value.Foreground = (Brush)FindResource(live || done ? "TextPrimary" : "TextDimmed");
    }

    // ---------------------------------------------------------------- Share

    private sealed record SavedRow(SavedWifiNetwork Network, string Title, string Subtitle);

    private void EnsureSaved()
    {
        if (_saved is not null) return;
        _saved = _wifi.Client.Ok ? _wifi.Client.GetSavedNetworks() : [];
        var link = _wifi.Link;
        // the connected network first: it's the one people usually want to share
        var ordered = _saved.OrderByDescending(s => link.Connected && s.Profile == link.Profile).ToList();
        SavedList.ItemsSource = ordered.Select(s => new SavedRow(s, s.Ssid,
            link.Connected && s.Profile == link.Profile ? $"Connected  ·  {link.Auth}" : s.Auth.Length > 0 ? s.Auth : "Saved")).ToList();
        SavedHeader.Text = $"SAVED NETWORKS  ·  {_saved.Count}";
        if (ordered.Count > 0) SavedList.SelectedIndex = 0;
        else ShowSaved(null);
    }

    private SavedWifiNetwork? Selected => (SavedList.SelectedItem as SavedRow)?.Network;

    private void OnSavedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true; // keep it from reaching the section TabControl
        ShowSaved(Selected);
    }

    private void ShowSaved(SavedWifiNetwork? n)
    {
        ShareDetail.Visibility = n is null ? Visibility.Hidden : Visibility.Visible;
        QrImage.Modules = null;
        QrImage.Redraw();
        if (n is null) return;

        var link = _wifi.Link;
        ShareSsid.Text = n.Ssid;
        ShareAuth.Text = link.Connected && n.Profile == link.Profile ? link.Auth : n.Auth.Length > 0 ? n.Auth : "Saved network";

        var hasKey = n is { Revealed: true, Protected: false };
        var dots = new string('•', 10);
        PasswordText.Text = n.Open ? "None. Anyone can join." : hasKey ? n.Key ?? "" : dots;
        PasswordText.Foreground = (Brush)FindResource(n.Open ? "WarningBrush" : hasKey ? "TextPrimary" : "TextDimmed");
        BtnReveal.Visibility = !n.Open && !n.Revealed && !n.Enterprise ? Visibility.Visible : Visibility.Collapsed;
        BtnCopy.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        BtnHide.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        BtnCopy.Content = "Copy";

        var showQr = n.Open || hasKey;
        ShareHint.Foreground = (Brush)FindResource(n is { Revealed: true, Protected: true } ? "WarningBrush" : "TextSecondary");
        ShareHint.Text = n switch
        {
            { Enterprise: true } => "Enterprise networks sign in with an account, not a shared password, so there's nothing to share as a QR code.",
            { Revealed: true, Protected: true } => "Windows keeps saved passwords encrypted and only decrypts them for administrators. Run JackOfAll as administrator to reveal it.",
            _ when showQr => "Point a phone camera at the code to join. No typing, no spelling out the password.",
            _ => "Reveal the password to make a QR code that phones can scan to join instantly.",
        };

        if (showQr)
        {
            QrImage.Modules = QrCodeService.Encode(QrCodeService.WifiPayload(n.Ssid, n.QrType, n.Key));
            QrImage.Redraw();
        }
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } n) return;
        _wifi.Client.Reveal(n);
        ShowSaved(n);
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } n) return;
        n.Revealed = false;
        n.Key = null;
        ShowSaved(n);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Key is not { } key) return;
        var data = new DataObject();
        data.SetText(key);
        // asks clipboard history (Windows' and JackOfAll's own) not to keep the password
        data.SetData(ClipboardHistoryService.ExcludeFromHistoryFormat, new MemoryStream(new byte[4]));
        try
        {
            Clipboard.SetDataObject(data, true);
            BtnCopy.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            BtnCopy.Content = "Clipboard busy";
        }
    }

    // ---------------------------------------------------------------- state

    public Dictionary<string, string> SaveState() => new()
    {
        ["section"] = Sections.SelectedIndex.ToString(),
        ["beeps"] = _beeper.Enabled ? "1" : "0",
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("section", out var s) && int.TryParse(s, out var idx) && idx is >= 0 and <= LanTab)
            Sections.SelectedIndex = idx;
        if (state.TryGetValue("beeps", out var b))
        {
            _beeper.Enabled = b == "1";
            UpdateBeepsButton();
        }
    }
}

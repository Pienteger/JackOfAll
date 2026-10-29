using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services.Undertow;

namespace JackOfAll.UserControls;

/// <summary>Undertow: why the PC is slow to boot. Boot timing, startup apps, services, logon tasks and network connections.</summary>
public partial class UndertowToolView : UserControl, IStatefulTool
{
    private readonly HashSet<int> _loadedTabs = [];
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(5) };
    private List<BootRecord> _boots = [];
    private List<ServiceItem> _services = [];
    private List<TaskItem> _tasks = [];
    private List<NetConnection> _connections = [];
    private bool _busy;

    public UndertowToolView()
    {
        InitializeComponent();
        ShieldGlyph.Visibility = Elevated.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        BtnReadBoot.ToolTip = Elevated.IsAdmin
            ? "Read the boot timing Windows logged"
            : "Windows only lets administrators read boot timing, so this asks once for permission";
        _live.Tick += (_, _) => { if (Tabs.SelectedIndex == 4 && !_busy) _ = LoadNetwork(); };
        Loaded += (_, _) => { LoadTab(Tabs.SelectedIndex); if (LiveNetwork.IsChecked == true) _live.Start(); };
        Unloaded += (_, _) => _live.Stop();
    }

    public Dictionary<string, string> SaveState() => new()
    {
        ["tab"] = Tabs.SelectedIndex.ToString(),
        ["hideMsServices"] = (HideMsServices.IsChecked == true).ToString(),
        ["hideMsTasks"] = (HideMsTasks.IsChecked == true).ToString()
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("hideMsServices", out var s)) HideMsServices.IsChecked = s == bool.TrueString;
        if (state.TryGetValue("hideMsTasks", out var t)) HideMsTasks.IsChecked = t == bool.TrueString;
        if (state.TryGetValue("tab", out var tab) && int.TryParse(tab, out var i) && i >= 0 && i < Tabs.Items.Count)
            Tabs.SelectedIndex = i;
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs || !IsLoaded) return;
        LoadTab(Tabs.SelectedIndex);
    }

    private void LoadTab(int index)
    {
        if (index == 5) { LoadChanges(); return; } // always fresh
        if (!_loadedTabs.Add(index)) return;
        switch (index)
        {
            case 0: ShowBoots(BootTiming.LoadCached(), BootTiming.CachedAt); break;
            case 1: _ = LoadStartup(); break;
            case 2: _ = LoadServices(); break;
            case 3: _ = LoadTasks(); break;
            case 4: _ = LoadNetwork(); break;
        }
    }

    private async Task Run(string doing, Func<Task> work)
    {
        _busy = true;
        Status(doing);
        try { await work(); }
        catch (OperationCanceledException ex) { Status(ex.Message); }
        catch (Exception ex) { Status($"Failed: {ex.Message}"); }
        finally { _busy = false; }
    }

    private void Status(string text) => StatusText.Text = text;

    // ── boot ───────────────────────────────────────────────────

    private async void OnReadBootClick(object sender, RoutedEventArgs e)
    {
        BtnReadBoot.IsEnabled = false;
        await Run("Reading the boot log…", async () =>
        {
            var boots = await Task.Run(BootTiming.Read);
            ShowBoots(boots, DateTime.Now);
            Status(boots.Count == 0 ? "Windows has no boot timing recorded yet." : $"Read {boots.Count} boots.");
        });
        BtnReadBoot.IsEnabled = true;
    }

    private void ShowBoots(List<BootRecord> boots, DateTime? readAt)
    {
        _boots = boots;
        BootGrid.ItemsSource = boots;
        var up = DateTime.Now - BootTiming.LastBoot;
        var upText = up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : $"{up.Hours} h {up.Minutes} min";

        if (boots.Count == 0)
        {
            BootHeadline.Text = $"Last started {BootTiming.LastBoot:ddd d MMM, HH:mm} (up {upText})";
            BootSubline.Text = "Read boot timing to see how long each boot took and what slowed it down.";
        }
        else
        {
            var last = boots[0];
            var typical = boots.Take(10).Select(b => b.BootMs).OrderBy(x => x).ElementAt(Math.Min(boots.Count, 10) / 2);
            BootHeadline.Text = $"Last recorded boot took {BoottimeText(last.BootMs)}: {BoottimeText(last.MainPathMs)} to the desktop, then {BoottimeText(last.PostBootMs)} settling down";
            BootSubline.Text = $"Typical recent boot {BoottimeText(typical)} · up {upText} · read {readAt:d MMM HH:mm}" +
                               (Elevated.IsAdmin ? "" : " · reading again asks for administrator rights");
            BootGrid.SelectedIndex = 0;
        }
        MarkSlowStartupApps();
    }

    private static string BoottimeText(double ms) => ms <= 0 ? "–" : $"{ms / 1000:0.0} s";

    private void OnBootSelected(object sender, SelectionChangedEventArgs e)
    {
        PhasePanel.Children.Clear();
        if (BootGrid.SelectedItem is not BootRecord boot) { CulpritGrid.ItemsSource = null; return; }
        CulpritGrid.ItemsSource = boot.Culprits;

        var max = boot.Phases.Count > 0 ? boot.Phases.Max(p => p.Ms) : 1;
        foreach (var p in boot.Phases.OrderByDescending(p => p.Ms))
        {
            var name = new TextBlock { Text = p.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var val = new TextBlock { Text = BoottimeText(p.Ms), Width = 56, TextAlignment = TextAlignment.Right };
            val.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var head = new DockPanel();
            DockPanel.SetDock(val, Dock.Right);
            head.Children.Add(val);
            head.Children.Add(name);

            var bar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, p.Name.StartsWith("After") ? "WarningBrush" : "AccentBrush");
            var track = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            var holder = new StackPanel();
            holder.Children.Add(head);
            holder.Children.Add(track);
            track.Children.Add(bar);
            track.SizeChanged += (_, _) => bar.Width = Math.Max(2, track.ActualWidth * p.Ms / max);
            PhasePanel.Children.Add(holder);
        }
    }

    // ── startup apps ───────────────────────────────────────────

    private void OnRefreshStartupClick(object sender, RoutedEventArgs e) => _ = LoadStartup();

    private async Task LoadStartup()
    {
        await Run("Reading startup apps…", async () =>
        {
            var apps = await Task.Run(StartupAudit.GetStartupApps);
            StartupGrid.ItemsSource = apps;
            MarkSlowStartupApps();
            var on = apps.Count(a => a.Enabled);
            StartupSummary.Text = $"{on} of {apps.Count} apps start with Windows. Turning one off works like Task Manager's Startup page; nothing is deleted.";
            Status("");
        });
    }

    /// <summary>Marks startup apps the boot log blamed for a slow boot.</summary>
    private void MarkSlowStartupApps()
    {
        if (StartupGrid.ItemsSource is not List<StartupApp> apps) return;
        var slow = _boots.SelectMany(b => b.Culprits).Where(c => c.Kind == "App")
            .GroupBy(c => Path.GetFileName(c.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(c => c.SlowedByMs), StringComparer.OrdinalIgnoreCase);
        foreach (var a in apps)
            a.SlowedBoot = slow.TryGetValue(Path.GetFileName(a.ExePath), out var ms) ? $"+{ms / 1000:0.0} s" : "";
        StartupGrid.Items.Refresh();
    }

    private async void OnToggleStartupClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StartupApp app || _busy) return;
        var turnOn = !app.Enabled;
        await Run($"{(turnOn ? "Turning on" : "Turning off")} {app.Name}…", async () =>
        {
            await Task.Run(() => StartupAudit.SetStartupEnabled(app.ApprovedKey, turnOn));
            StartupAudit.Record(new ChangeEntry
            {
                When = DateTime.Now, Kind = "Startup app", Key = app.ApprovedKey, Name = app.Name,
                Before = app.StateText, After = turnOn ? "On" : "Off"
            });
            await LoadStartup();
            Status($"{app.Name} will {(turnOn ? "" : "no longer ")}start with Windows. Undo it on the Changes tab.");
        });
    }

    // ── services ───────────────────────────────────────────────

    private void OnRefreshServicesClick(object sender, RoutedEventArgs e) => _ = LoadServices();
    private void OnServiceFilterClick(object sender, RoutedEventArgs e) => ShowServices();

    private async Task LoadServices()
    {
        await Run("Reading services…", async () =>
        {
            _services = await Task.Run(StartupAudit.GetServices);
            ShowServices();
            Status("");
        });
    }

    private void ShowServices()
    {
        var changed = StartupAudit.LoadChanges().Where(c => c.Kind == "Service").Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hideMs = HideMsServices.IsChecked == true;
        var shown = _services.Where(s => (s.IsAutomatic || changed.Contains(s.Name)) && !(hideMs && s.IsMicrosoft)).ToList();
        ServiceGrid.ItemsSource = shown;
        var auto = _services.Count(s => s.IsAutomatic);
        var third = _services.Count(s => s.IsAutomatic && !s.IsMicrosoft);
        ServiceSummary.Text = $"{auto} services start automatically, {third} of them not from Microsoft. Manual services still start when something needs them.";
    }

    private async void OnToggleServiceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServiceItem svc || _busy) return;
        string target;
        if (svc.IsAutomatic)
        {
            target = ServiceModes.Manual;
            if (svc.IsMicrosoft)
            {
                var dlg = new ConfirmationDialog("Undertow", "Change a Windows service?",
                    $"{svc.DisplayName} comes from Microsoft. Windows or a driver may need it at startup. You can undo this on the Changes tab.",
                    "Make manual") { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() != true) return;
            }
        }
        else
        {
            // Put back whatever it was before Undertow touched it, or plain automatic.
            target = StartupAudit.LoadChanges().FirstOrDefault(c => c.Kind == "Service" && c.Key == svc.Name && !c.Undone)?.Before
                     ?? ServiceModes.Automatic;
            if (target is not (ServiceModes.Automatic or ServiceModes.Delayed)) target = ServiceModes.Automatic;
        }

        await Run($"Setting {svc.DisplayName} to {target}…", async () =>
        {
            await Task.Run(() => StartupAudit.SetServiceMode(svc.Name, target));
            StartupAudit.Record(new ChangeEntry
            {
                When = DateTime.Now, Kind = "Service", Key = svc.Name, Name = svc.DisplayName,
                Before = svc.StartMode, After = target
            });
            await LoadServices();
            Status($"{svc.DisplayName} now starts: {target}. Undo it on the Changes tab.");
        });
    }

    // ── scheduled tasks ────────────────────────────────────────

    private void OnRefreshTasksClick(object sender, RoutedEventArgs e) => _ = LoadTasks();
    private void OnTaskFilterClick(object sender, RoutedEventArgs e) => ShowTasks();

    private async Task LoadTasks()
    {
        await Run("Reading scheduled tasks…", async () =>
        {
            _tasks = await Task.Run(StartupAudit.GetTasks);
            ShowTasks();
            Status("");
        });
    }

    private void ShowTasks()
    {
        var hideMs = HideMsTasks.IsChecked == true;
        TaskGrid.ItemsSource = _tasks.Where(t => !(hideMs && t.IsMicrosoft)).ToList();
        var on = _tasks.Count(t => t.Enabled && !t.IsMicrosoft);
        TaskSummary.Text = $"{_tasks.Count} tasks run at startup or log on; {on} of the ones not from Windows are turned on.";
    }

    private async void OnToggleTaskClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskItem task || _busy) return;
        var turnOn = !task.Enabled;
        await Run($"{(turnOn ? "Turning on" : "Turning off")} {task.Name}…", async () =>
        {
            await Task.Run(() => StartupAudit.SetTaskEnabled(task.Path, turnOn));
            StartupAudit.Record(new ChangeEntry
            {
                When = DateTime.Now, Kind = "Scheduled task", Key = task.Path, Name = task.Path,
                Before = task.StateText, After = turnOn ? "On" : "Off"
            });
            await LoadTasks();
            Status($"{task.Name} is turned {(turnOn ? "on" : "off")}. Undo it on the Changes tab.");
        });
    }

    // ── network ────────────────────────────────────────────────

    private void OnRefreshNetworkClick(object sender, RoutedEventArgs e) => _ = LoadNetwork();

    private void OnLiveClick(object sender, RoutedEventArgs e)
    {
        if (LiveNetwork.IsChecked == true) { _live.Start(); _ = LoadNetwork(); }
        else _live.Stop();
    }

    private void OnNetFilterChanged(object sender, TextChangedEventArgs e) => ShowNetwork();

    private async Task LoadNetwork()
    {
        var includeLocal = IncludeLocal.IsChecked == true;
        await Run("Reading connections…", async () =>
        {
            _connections = await Task.Run(() => NetworkConnections.Get(includeLocal));
            ShowNetwork();
            NetworkConnections.ResolveHosts(_connections, a => Dispatcher.BeginInvoke(a));
            Status("");
        });
    }

    private void ShowNetwork()
    {
        var f = NetFilter.Text.Trim();
        var shown = f.Length == 0
            ? _connections
            : _connections.Where(c => c.Process.Contains(f, StringComparison.OrdinalIgnoreCase)
                                      || c.Remote.Contains(f, StringComparison.OrdinalIgnoreCase)
                                      || c.Host.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = NetGrid.SelectedItem as NetConnection;
        NetGrid.ItemsSource = shown;
        if (selected is not null)
            NetGrid.SelectedItem = shown.FirstOrDefault(c => c.Pid == selected.Pid && c.Remote == selected.Remote && c.LocalPort == selected.LocalPort);
        var procs = _connections.Select(c => c.Pid).Distinct().Count();
        var hosts = _connections.Where(c => c.State != "Listening").Select(c => c.Remote).Distinct().Count();
        NetSummary.Text = $"{procs} processes, {_connections.Count} connections to {hosts} addresses.";
    }

    // ── changes ────────────────────────────────────────────────

    private void LoadChanges() => ChangeGrid.ItemsSource = StartupAudit.LoadChanges();

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChangeEntry change || _busy || change.Undone) return;
        await Run($"Undoing {change.Name}…", async () =>
        {
            await Task.Run(() => StartupAudit.Undo(change));
            LoadChanges();
            _loadedTabs.Remove(change.Kind switch { "Startup app" => 1, "Service" => 2, _ => 3 });
            Status($"{change.Name} is back to {change.Before}.");
        });
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services.SpaceLens;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

/// <summary>
/// SpaceLens: a treemap of where disk space went, plus Crow's clean up of regenerable build output and caches.
/// </summary>
public partial class SpaceLensToolView : UserControl, IStatefulTool, IDisposable
{
    private const int TopFiles = 250;
    private static readonly Brush[] Swatches = Cats.Colors.Select(c => Frozen(Color.FromRgb(c.R, c.G, c.B))).ToArray();

    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Scanner? _scanner;
    private readonly Stopwatch _watch = new();
    private Node? _root, _current;
    private string? _lastPath;
    private ObservableCollection<JunkRow> _junk = [];
    private bool _junkStale = true, _junkBusy;
    private int _junkGeneration;

    public SpaceLensToolView()
    {
        InitializeComponent();
        LoadDrives();

        Map.HoverChanged += n => ShowInfo(n ?? Map.Selected);
        Map.NodeClicked += OnMapClick;
        Map.NodeDoubleClicked += n =>
        {
            if (n.IsDir) ZoomTo(n);
            else if (n.Parent != _current) ZoomTo(n.Parent);
        };
        Map.Wheel += (delta, n) =>
        {
            if (_current is null) return;
            if (delta < 0) { Up(); return; }
            var c = n;
            while (c is not null && c.Parent != _current) c = c.Parent;
            if (c is { IsDir: true }) ZoomTo(c);
        };
        Map.BackRequested += Up;

        _scanTimer.Tick += (_, _) =>
        {
            if (_scanner is not { } sc) return;
            ScanLine1.Text = $"{Fmt.N(Interlocked.Read(ref sc.Files))} files  ·  {Fmt.Size(Interlocked.Read(ref sc.Bytes))}";
            ScanLine2.Text = sc.Current;
        };

        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            var p = File.Exists(paths[0]) ? Path.GetDirectoryName(paths[0]) : paths[0];
            if (p is not null) StartScan(p);
        };

        PreviewKeyDown += OnKey;
        ThemeNotifier.ThemeChanged += OnThemeChanged;
        JunkGrid.ItemsSource = _junk;
    }

    private void OnThemeChanged() => Map.Rebuild();

    // ── drives and scanning ────────────────────────────────────────

    private void LoadDrives()
    {
        var sys = Path.GetPathRoot(Environment.SystemDirectory);
        var items = new List<DriveOption>();
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                if (!di.IsReady) continue;
                var label = string.IsNullOrEmpty(di.VolumeLabel)
                    ? di.DriveType switch { DriveType.Removable => "USB drive", DriveType.Network => "Network", _ => "Local Disk" }
                    : di.VolumeLabel;
                items.Add(new DriveOption(di.Name,
                    $"{di.Name.TrimEnd('\\')}   {label}   ·   {Fmt.Size(di.AvailableFreeSpace)} free of {Fmt.Size(di.TotalSize)}"));
            }
            catch
            {
                // Drive vanished or is locked; skip it.
            }
        }

        DriveBox.ItemsSource = items;
        DriveBox.SelectedItem = items.FirstOrDefault(d => string.Equals(d.Path, sys, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
    }

    private void OnScanClick(object sender, RoutedEventArgs e)
    {
        if (_scanner is not null)
        {
            _scanner.Cancel = true;
            ScanLabel.Text = "Stopping…";
            return;
        }

        if (DriveBox.SelectedItem is DriveOption d) StartScan(d.Path);
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder to analyze" };
        if (_current is not null) dlg.InitialDirectory = _current.FullPath;
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) StartScan(dlg.FolderName);
    }

    private async void StartScan(string path)
    {
        if (_scanner is not null) return;
        try { path = Path.GetFullPath(path); } catch { /* keep as typed */ }
        if (!Directory.Exists(path))
        {
            StatusText.Text = $"That folder doesn't exist: {path}";
            return;
        }
        if (path.Length > 3 && path.EndsWith('\\')) path = path.TrimEnd('\\');

        _root = _current = null;
        Map.Selected = null;
        Map.SetHighlight(-1, null, null);
        Map.SetRoot(null);
        LegendList.ItemsSource = null;
        ShareBar.Children.Clear();
        ShareBar.ColumnDefinitions.Clear();
        FilesGrid.ItemsSource = null;
        TypesGrid.ItemsSource = null;
        Crumbs.Children.Clear();
        BtnUp.IsEnabled = false;
        _junkStale = true;

        EmptyState.Visibility = Visibility.Collapsed;
        ScanState.Visibility = Visibility.Visible;
        ScanLine1.Text = "Scanning…";
        ScanLine2.Text = path;
        ScanGlyph.Text = "";
        ScanLabel.Text = "Stop";
        BtnFolder.IsEnabled = DriveBox.IsEnabled = BtnCleanup.IsEnabled = false;
        StatsText.Text = "";
        StatusText.Text = $"Scanning {path} …";

        var sc = _scanner = new Scanner();
        _watch.Restart();
        _scanTimer.Start();
        Node? result = null;
        Exception? error = null;
        try { result = await Task.Run(() => sc.Run(path)); }
        catch (Exception ex) { error = ex; }

        _scanTimer.Stop();
        _watch.Stop();
        _scanner = null;
        ScanState.Visibility = Visibility.Collapsed;
        ScanGlyph.Text = "";
        ScanLabel.Text = "Scan";
        BtnFolder.IsEnabled = DriveBox.IsEnabled = BtnCleanup.IsEnabled = true;

        if (result is null)
        {
            EmptyState.Visibility = Visibility.Visible;
            StatusText.Text = "Scan failed: " + (error?.Message ?? "unknown error");
            return;
        }

        _root = result;
        _lastPath = path;
        ZoomTo(result);

        var stats = $"{Fmt.N(sc.Files)} files  ·  {Fmt.N(sc.Dirs)} folders  ·  {_watch.Elapsed.TotalSeconds:0.0} s";
        if (sc.Cancel) stats = "Stopped early  ·  " + stats;
        if (sc.Denied > 0) stats += $"  ·  {Fmt.N(sc.Denied)} folders skipped (no access)";
        try
        {
            if (result.Name.Length == 3 && result.Name[1] == ':')
            {
                var di = new DriveInfo(result.Name);
                stats += $"  ·  disk {Fmt.Pct(di.TotalSize - di.TotalFreeSpace, di.TotalSize)} full";
            }
        }
        catch
        {
            // Not a drive root we can query.
        }

        StatsText.Text = stats;
        ShowInfo(null);
        if (Tabs.SelectedItem == CleanupTab) FindJunk();
    }

    // ── navigation ─────────────────────────────────────────────────

    private void ZoomTo(Node? n)
    {
        if (n is not { IsDir: true }) return;
        _current = n;
        Map.SetRoot(n);
        BtnUp.IsEnabled = n.Parent is not null;
        BuildCrumbs();
        RefreshSide();
        ShowInfo(Map.Selected);
    }

    private void Up()
    {
        if (_current?.Parent is null) return;
        var prev = _current;
        ZoomTo(_current.Parent);
        Select(prev);
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => Up();

    private void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        if (_current is null) return;
        var chain = new List<Node>();
        for (var x = _current; x is not null; x = x.Parent) chain.Insert(0, x);
        var start = Math.Max(0, chain.Count - 6);
        if (start > 0) Crumbs.Children.Add(CrumbText("…  ›", "TextDimmed"));
        for (var i = start; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i == chain.Count - 1)
            {
                var last = CrumbText(node.Name, "TextPrimary");
                last.FontWeight = FontWeights.SemiBold;
                Crumbs.Children.Add(last);
                Crumbs.Children.Add(CrumbText($"{Fmt.Size(node.Size)}  ·  {Fmt.N(node.Files)} files", "TextSecondary"));
                continue;
            }

            var link = new Button
            {
                Content = node.Name,
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(4, 1, 4, 1),
                Cursor = Cursors.Hand
            };
            link.SetResourceReference(ForegroundProperty, "AccentBrush");
            link.Click += (_, _) => ZoomTo(node);
            Crumbs.Children.Add(link);
            Crumbs.Children.Add(CrumbText("›", "TextDimmed"));
        }
    }

    private static TextBlock CrumbText(string text, string brushKey)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    private void Select(Node? n)
    {
        Map.Selected = n;
        Map.InvalidateVisual();
        ShowInfo(n);
    }

    private void ShowInfo(Node? n)
    {
        if (n is null || _current is null)
        {
            StatusText.Text = _current is not null
                ? "Click a tile to select  ·  double-click or scroll up to zoom in  ·  Backspace or scroll down to go up  ·  right-click for actions"
                : "Pick a drive and press Scan.";
            return;
        }

        var s = $"{n.FullPath}     {Fmt.Size(n.Size)}";
        if (n != _current && n.IsUnder(_current)) s += $"  ({Fmt.Pct(n.Size, _current.Size)} of this view)";
        if (n.IsDir) s += $"  ·  {Fmt.N(n.Files)} files";
        StatusText.Text = s;
    }

    private void OnMapClick(Node n, MouseButtonEventArgs e)
    {
        Select(n);
        if (e.ChangedButton == MouseButton.Right) ShowMenu(n, Map);
    }

    // ── highlight and side panel ───────────────────────────────────

    private void OnLegendClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int cat }) return;
        var next = Map.HiCat == cat ? -1 : cat;
        Map.SetHighlight(next, null, null);
        RefreshLegend();
        StatusText.Text = next >= 0 ? $"Highlighting {Cats.Names[next]}  ·  click it again or press Esc to clear" : "";
    }

    private void OnTypeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TypesGrid.SelectedItem is not TypeRow row) return;
        Map.SetHighlight(-1, row.Ext, null);
        RefreshLegend();
        StatusText.Text = $"Highlighting {(row.Ext.Length == 0 ? "files without an extension" : row.Ext + " files")}  ·  press Esc to clear";
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileRow row) Select(row.Node);
    }

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileRow row) Reveal(row.Node);
    }

    private void OnFileRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FilesGrid, e.OriginalSource as DependencyObject) is not DataGridRow { Item: FileRow row } container) return;
        container.IsSelected = true;
        ShowMenu(row.Node, FilesGrid);
        e.Handled = true;
    }

    private long[] _catSizes = new long[Cats.Count];

    private void RefreshSide()
    {
        if (_current is null) return;
        var catSize = new long[Cats.Count];
        var ext = new Dictionary<string, long[]>();
        var top = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(_current);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d.Children is null) continue;
            foreach (var c in d.Children)
            {
                if (c.IsDir) { stack.Push(c); continue; }
                catSize[c.Cat] += c.Size;
                if (!ext.TryGetValue(c.Ext, out var a)) ext[c.Ext] = a = [0, 0, c.Cat];
                a[0] += c.Size;
                a[1]++;
                if (top.Count < TopFiles || c.Size > top[^1].Size)
                {
                    int lo = 0, hi = top.Count;
                    while (lo < hi) { var mid = (lo + hi) / 2; if (top[mid].Size >= c.Size) lo = mid + 1; else hi = mid; }
                    top.Insert(lo, c);
                    if (top.Count > TopFiles) top.RemoveAt(TopFiles);
                }
            }
        }

        _catSizes = catSize;
        RefreshLegend();

        var cur = _current.FullPath;
        FilesGrid.ItemsSource = top.Select(f =>
        {
            var folder = f.Parent.FullPath;
            folder = folder.StartsWith(cur, StringComparison.OrdinalIgnoreCase) ? folder[cur.Length..].TrimStart('\\') : folder;
            return new FileRow(f, Swatches[f.Cat], f.Name, Fmt.Size(f.Size), folder.Length == 0 ? "(here)" : folder);
        }).ToList();

        var total = _current.Size;
        TypesGrid.ItemsSource = ext.OrderByDescending(kv => kv.Value[0]).Take(400)
            .Select(kv => new TypeRow(kv.Key, Swatches[kv.Value[2]], kv.Key.Length == 0 ? "(no extension)" : kv.Key,
                Fmt.Size(kv.Value[0]), Fmt.N(kv.Value[1]), Fmt.Pct(kv.Value[0], total)))
            .ToList();
    }

    private void RefreshLegend()
    {
        var total = _catSizes.Sum();
        var active = Map.HiCat;
        var order = Enumerable.Range(0, Cats.Count).OrderByDescending(c => _catSizes[c]).ToList();
        LegendList.ItemsSource = order.Select(c => new LegendRow(c, Swatches[c], Cats.Names[c], Fmt.Size(_catSizes[c]),
            Fmt.Pct(_catSizes[c], total), _catSizes[c] == 0 || (active >= 0 && c != active) ? 0.45 : 1.0)).ToList();

        ShareBar.Children.Clear();
        ShareBar.ColumnDefinitions.Clear();
        if (total <= 0) return;
        foreach (var c in order.Where(c => _catSizes[c] > 0))
        {
            ShareBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_catSizes[c], GridUnitType.Star) });
            var block = new Border { Background = Swatches[c], Opacity = active >= 0 && c != active ? 0.3 : 1 };
            Grid.SetColumn(block, ShareBar.ColumnDefinitions.Count - 1);
            ShareBar.Children.Add(block);
        }
    }

    // ── actions ────────────────────────────────────────────────────

    private void ShowMenu(Node n, UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        var zoom = new MenuItem
        {
            Header = n.IsDir ? "Zoom into this folder" : "Zoom into its folder",
            IsEnabled = n.IsDir ? n != _current : n.Parent != _current
        };
        zoom.Click += (_, _) => ZoomTo(n.IsDir ? n : n.Parent);
        var open = new MenuItem { Header = n.IsDir ? "Open folder" : "Open file" };
        open.Click += (_, _) => Open(n);
        var reveal = new MenuItem { Header = "Show in File Explorer" };
        reveal.Click += (_, _) => Reveal(n);
        var copy = new MenuItem { Header = "Copy path" };
        copy.Click += (_, _) => CopyPath(n);
        var recycle = new MenuItem { Header = $"Send to Recycle Bin  ({Fmt.Size(n.Size)})…", IsEnabled = n.Parent is not null && n != _current };
        recycle.Click += (_, _) => Recycle(n);
        menu.Items.Add(zoom);
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(recycle);
        menu.IsOpen = true;
    }

    private void Open(Node n)
    {
        try { Process.Start(new ProcessStartInfo(n.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Reveal(Node n)
    {
        try { Process.Start("explorer.exe", $"/select,\"{n.FullPath}\""); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void CopyPath(Node n)
    {
        try
        {
            Clipboard.SetText(n.FullPath);
            StatusText.Text = "Copied: " + n.FullPath;
        }
        catch
        {
            // Clipboard busy; nothing to do.
        }
    }

    private void Recycle(Node n)
    {
        if (n.Parent is null || n == _current) return;
        var path = n.FullPath;
        var what = n.IsDir ? $"folder ({Fmt.N(n.Files)} files, {Fmt.Size(n.Size)})" : $"file ({Fmt.Size(n.Size)})";
        if (MessageBox.Show($"Move this {what} to the Recycle Bin?\n\n{path}", "SpaceLens",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var owner = Window.GetWindow(this) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;
        Mouse.OverrideCursor = Cursors.Wait;
        bool ok;
        string? error;
        try { ok = Recycler.Send(path, owner, out error); }
        finally { Mouse.OverrideCursor = null; }

        if (!ok)
        {
            if (error != "Cancelled.") MessageBox.Show($"Couldn't move it to the Recycle Bin:\n\n{error}", "SpaceLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        n.Detach();
        if (Map.Selected?.IsUnder(n) == true) Map.Selected = null;
        _junkStale = true;
        ZoomTo(_current);
        StatusText.Text = $"Moved to Recycle Bin: {path}  ({Fmt.Size(n.Size)}; empty the Recycle Bin to actually free it)";
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { Tabs.SelectedItem = CleanupTab; e.Handled = true; return; }
        if (Tabs.SelectedItem == CleanupTab) return;
        switch (e.Key)
        {
            case Key.Back:
                Up();
                e.Handled = true;
                break;
            case Key.F5 when _lastPath is not null && _scanner is null:
                StartScan(_lastPath);
                e.Handled = true;
                break;
            case Key.Delete when Map.Selected is not null:
                Recycle(Map.Selected);
                e.Handled = true;
                break;
            case Key.Escape:
                Map.Selected = null;
                Map.SetHighlight(-1, null, null);
                RefreshLegend();
                ShowInfo(null);
                e.Handled = true;
                break;
        }
    }

    // ── clean up (Crow) ────────────────────────────────────────────

    private void OnCleanupClick(object sender, RoutedEventArgs e) => Tabs.SelectedItem = CleanupTab;

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs) return;
        if (Tabs.SelectedItem == CleanupTab && _junkStale && !_junkBusy) FindJunk();
    }

    private void OnFindJunkClick(object sender, RoutedEventArgs e) => FindJunk();

    private async void FindJunk()
    {
        if (_junkBusy) return;
        _junkStale = false;
        var generation = ++_junkGeneration;
        _junk = [];
        JunkGrid.ItemsSource = _junk;
        CleanupNote.Text = _root is not null
            ? $"Build output (bin, obj, node_modules, target...) sitting next to its project file in {_root.FullPath}, plus temp files and caches. Everything here comes back on its own: build or restore the project, and apps recreate their caches."
            : "Temp files and caches. Scan a folder on the Map tab first to also find build output (bin, obj, node_modules, target...).";

        if (_root is not null)
            foreach (var j in BuildJunk.Find(_root)) AddJunk(j);

        JunkEmpty.Text = "Measuring temp files and caches…";
        JunkEmpty.Visibility = Visibility.Visible;
        SetJunkBusy(true);
        List<JunkItem> system;
        try { system = await Task.Run(() => SystemJunk.Measure(SystemJunk.GetLocations())); }
        catch { system = []; }
        if (generation != _junkGeneration) return;
        foreach (var j in system) AddJunk(j);
        SetJunkBusy(false);
        JunkEmpty.Text = "Nothing to clean. Nice.";
        JunkEmpty.Visibility = _junk.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateJunkSummary();
    }

    private void AddJunk(JunkItem j)
    {
        var where = j.IsBuild && _root is not null && j.Path.StartsWith(_root.FullPath, StringComparison.OrdinalIgnoreCase)
            ? j.Path[_root.FullPath.Length..].TrimStart('\\')
            : j.Path;
        var type = j.IsBuild ? $"Build output  ·  {j.Kind}" : j.Kind + (j.RequiresAdmin ? "  ·  needs admin" : "");
        var row = new JunkRow(j, type, where);
        row.PropertyChanged += (_, _) => UpdateJunkSummary();
        _junk.Add(row);
        JunkEmpty.Visibility = Visibility.Collapsed;
    }

    private void SetJunkBusy(bool busy)
    {
        _junkBusy = busy;
        BtnFindJunk.IsEnabled = !busy;
        JunkGrid.IsEnabled = !busy;
        UpdateJunkSummary();
    }

    private void UpdateJunkSummary()
    {
        var picked = _junk.Where(r => r.Checked).ToList();
        var size = picked.Sum(r => r.Item.Size);
        BtnShowJunk.IsEnabled = !_junkBusy && _junk.Any(r => r.Item.IsBuild);
        BtnDeleteJunk.IsEnabled = !_junkBusy && picked.Count > 0;
        DeleteLabel.Text = picked.Count > 0 ? $"Delete {picked.Count} selected  ({Fmt.Size(size)})" : "Delete selected";
        if (!_junkBusy && _junk.Count > 0)
            JunkSummary.Text = $"{_junk.Count} things found, {Fmt.Size(_junk.Sum(r => r.Item.Size))} in total. The safe ones are ticked.";
    }

    private void OnShowJunkClick(object sender, RoutedEventArgs e)
    {
        var build = _junk.Where(r => r.Item.IsBuild).Select(r => r.Item).ToList();
        if (build.Count == 0 || _root is null) return;
        Map.SetHighlight(-1, null, build.Select(j => j.Node).ToHashSet());
        RefreshLegend();
        Tabs.SelectedIndex = 0;
        StatusText.Text = $"Highlighting {build.Count} build-output folders ({Fmt.Size(build.Sum(j => j.Size))}, red frames)  ·  press Esc to clear";
    }

    private async void OnDeleteJunkClick(object sender, RoutedEventArgs e)
    {
        var picked = _junk.Where(r => r.Checked).Select(r => r.Item).ToList();
        if (picked.Count == 0) return;
        var size = picked.Sum(j => j.Size);
        if (MessageBox.Show(
                $"Permanently delete {picked.Count} item(s), about {Fmt.Size(size)}?\n\n" +
                "This skips the Recycle Bin. Build output is regenerated by building or restoring the project; " +
                "temp files and caches are recreated by Windows and apps as needed. Files that are in use are skipped.",
                "Clean up", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        SetJunkBusy(true);
        BtnShowJunk.IsEnabled = BtnDeleteJunk.IsEnabled = false;
        var total = new CleanResult();
        var removed = new List<JunkItem>();
        var system = false;
        for (var i = 0; i < picked.Count; i++)
        {
            var j = picked[i];
            JunkSummary.Text = $"Removing {i + 1} of {picked.Count}:  {j.Name}  ({j.Kind})…";
            if (j.IsBuild)
            {
                total.Add(await Task.Run(() => Deleter.DeleteTree(j.Path)));
                if (!Directory.Exists(j.Path)) removed.Add(j);
            }
            else
            {
                total.Add(await Task.Run(() => SystemJunk.Clean(j.Location)));
                system = true;
            }
        }

        foreach (var j in removed)
        {
            if (_current?.IsUnder(j.Node) == true) _current = j.Node.Parent;
            if (Map.Selected?.IsUnder(j.Node) == true) Map.Selected = null;
            j.Node.Detach();
        }
        if (removed.Count > 0) ZoomTo(_current);

        foreach (var row in _junk.Where(r => picked.Contains(r.Item) && (!r.Item.IsBuild || removed.Contains(r.Item))).ToList())
            _junk.Remove(row);
        SetJunkBusy(false);

        var drive = "";
        try
        {
            var probe = picked[0].IsBuild ? picked[0].Path : picked[0].Location.Paths[0];
            var di = new DriveInfo(Path.GetPathRoot(probe)!);
            drive = $"  Drive {di.Name.TrimEnd('\\')} now has {Fmt.Size(di.AvailableFreeSpace)} free.";
        }
        catch
        {
            // No drive info; skip that sentence.
        }

        JunkSummary.Text = $"Freed {Fmt.Size(total.Freed)}  ·  {Fmt.N(total.Deleted)} items removed" +
                           (total.Skipped > 0 ? $"  ·  {Fmt.N(total.Skipped)} in use or protected, skipped" : "") + "." + drive;
        if (system && _root is not null)
            StatusText.Text = "Caches were cleaned; press F5 to rescan so the map reflects it.";
    }

    // ── IStatefulTool / IDisposable ────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>();
        if (_lastPath is not null) state["path"] = _lastPath;
        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (!state.TryGetValue("path", out var path) || !Directory.Exists(path)) return;
        _lastPath = path;
        if (DriveBox.ItemsSource is not List<DriveOption> items) return;
        var match = items.FirstOrDefault(d => string.Equals(d.Path.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new DriveOption(path, $"{path}   ·   last scanned folder");
            items.Insert(0, match);
            DriveBox.ItemsSource = null;
            DriveBox.ItemsSource = items;
        }
        DriveBox.SelectedItem = match;
        StatusText.Text = $"Press Scan to look at {path} again.";
    }

    public void Dispose()
    {
        if (_scanner is not null) _scanner.Cancel = true;
        _junkGeneration++;
        _scanTimer.Stop();
        ThemeNotifier.ThemeChanged -= OnThemeChanged;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

internal sealed record DriveOption(string Path, string Label)
{
    public override string ToString() => Label;
}

internal sealed record LegendRow(int Cat, Brush Swatch, string Name, string SizeText, string Pct, double Opacity);

internal sealed record FileRow(Node Node, Brush Swatch, string Name, string SizeText, string Folder);

internal sealed record TypeRow(string Ext, Brush Swatch, string Name, string SizeText, string Count, string Share);

internal sealed class JunkRow(JunkItem item, string type, string where) : INotifyPropertyChanged
{
    public JunkItem Item { get; } = item;
    public string Name => Item.Name;
    public string Type { get; } = type;
    public string SizeText => Fmt.Size(Item.Size);
    public string Where { get; } = where;

    public bool Checked
    {
        get => Item.Checked;
        set
        {
            if (Item.Checked == value) return;
            Item.Checked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

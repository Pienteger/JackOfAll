using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class LogViewerToolView : UserControl, IStatefulTool, IDisposable
{
    private const int PageSize = 2_000;

    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim _tailGate = new(1, 1);
    private List<LogLine> _memoryLines = [];
    private List<LogLine> _tailLines = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _tailDelayCts;
    private CancellationTokenSource? _tailCts;
    private FileSystemWatcher? _watcher;
    private LogFileIndex? _index;
    private string? _currentPath;
    private string _tailRemainder = "";
    private long _tailPosition;
    private int _currentPage;
    private bool _disposed;

    public LogViewerToolView()
    {
        InitializeComponent();
        _searchDelay.Tick += OnSearchDelayTick;

        SearchBox.GotFocus += (_, _) => SearchPlaceholder.Visibility = Visibility.Collapsed;
        SearchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                SearchPlaceholder.Visibility = Visibility.Visible;
        };
    }

    private async void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Log files|*.log;*.txt;*.jsonl;*.json;*.ndjson|All files|*.*",
            Title = "Open Log File"
        };
        if (dialog.ShowDialog() == true)
            await LoadFileAsync(dialog.FileName);
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_currentPath is not null)
            await LoadFileAsync(_currentPath);
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            ResetSession(clearPath: true);
            var cap = Math.Clamp(AppSettings.Current?.LogViewerRetainedEntries ?? 100_000, 10_000, 500_000);
            _memoryLines = ParseText(Clipboard.GetText(), cap);
            FileLabel.Text = "pasted content";
            ShowRows(_memoryLines, $"Showing {_memoryLines.Count:N0} pasted entries");
            ReloadButton.IsEnabled = false;
            TailToggle.IsEnabled = false;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Clipboard error: {exception.Message}";
        }
    }

    private async Task LoadFileAsync(string path)
    {
        ResetSession(clearPath: false);
        _currentPath = path;
        FileLabel.Text = Path.GetFileName(path);
        ReloadButton.IsEnabled = false;
        TailToggle.IsEnabled = false;

        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = "Indexing log...";
        LoadProgress.Value = 0;
        var progress = new Progress<double>(value => LoadProgress.Value = value);

        try
        {
            var scanMb = Math.Clamp(AppSettings.Current?.LogViewerScanLimitMb ?? 256, 16, 4096);
            var scanBytes = (long)scanMb * 1024 * 1024;
            var previewBytes = Math.Min(scanBytes, 4L * 1024 * 1024);
            _index = await LogFileIndexService.BuildAsync(
                path,
                previewBytes,
                PageSize,
                null,
                token);

            _currentPage = 0;
            await ShowPageAsync(0, token);

            if (_index.ScannedBytes < Math.Min(_index.FileLength, scanBytes))
            {
                LoadingText.Text = "First page ready. Indexing remaining log...";
                _index = await LogFileIndexService.BuildAsync(
                    path,
                    scanBytes,
                    PageSize,
                    progress,
                    token);
                await ShowPageAsync(0, token);
            }

            ReloadButton.IsEnabled = true;
            TailToggle.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Log loading cancelled.";
        }
        catch (Exception exception)
        {
            ShowEmpty();
            StatusText.Text = $"Unable to open log: {exception.Message}";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowPageAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (_index is null) return;
        var page = await LogFileIndexService.ReadPageAsync(_index, pageNumber, cancellationToken);
        _currentPage = pageNumber;
        LogGrid.ItemsSource = page.Lines;
        LogGrid.Visibility = Visibility.Visible;
        DropZone.Visibility = Visibility.Collapsed;
        PageControls.Visibility = _index.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        PageText.Text = $"Page {pageNumber + 1:N0} of {_index.PageCount:N0}";
        UpdateCounts(page.Lines);

        var qualifier = _index.IsTruncated
            ? $" Partial index: {_index.ScannedBytes / 1024.0 / 1024.0:F1} MB scan limit reached."
            : "";
        var shortened = page.ShortenedLine ? " One or more long rows were shortened." : "";
        StatusText.Text = $"Showing {page.Lines.Count:N0} rows from {_index.TotalLines:N0} indexed lines.{qualifier}{shortened}";
    }

    private async void OnPreviousPageClick(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 0 || _index is null) return;
        CancelFilter();
        await ShowPageAsync(_currentPage - 1);
    }

    private async void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if (_index is null || _currentPage + 1 >= _index.PageCount) return;
        CancelFilter();
        await ShowPageAsync(_currentPage + 1);
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await LoadFileAsync(files[0]);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ResetSession(clearPath: true);
        SearchBox.Text = "";
        FileLabel.Text = "";
        ShowEmpty();
        StatusText.Text = "Drop a file or paste logs to begin.";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _filterCts?.Cancel();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private async void OnSearchDelayTick(object? sender, EventArgs e)
    {
        _searchDelay.Stop();
        await ApplyFiltersAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        CancelFilter();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;
        var query = SearchBox.Text.Trim();
        var allowed = GetAllowedLevels();

        try
        {
            if (TailToggle.IsChecked == true)
            {
                var filteredTail = await Task.Run(
                    () => Filter(_tailLines, query, allowed, token),
                    token);
                ShowRows(filteredTail, $"Showing {filteredTail.Count:N0} of {_tailLines.Count:N0} tailed entries");
                return;
            }

            if (_index is not null)
            {
                if (string.IsNullOrEmpty(query) && allowed.Count == 6)
                {
                    await ShowPageAsync(_currentPage, token);
                    return;
                }

                LoadingPanel.Visibility = Visibility.Visible;
                LoadingText.Text = "Scanning filtered results...";
                LoadProgress.Value = 0;
                var retainedCap = Math.Clamp(AppSettings.Current?.LogViewerRetainedEntries ?? 100_000, 10_000, 500_000);
                var matches = new List<LogLine>(Math.Min(retainedCap, 4096));

                for (var pageNumber = 0; pageNumber < _index.PageCount && matches.Count < retainedCap; pageNumber++)
                {
                    token.ThrowIfCancellationRequested();
                    var page = await LogFileIndexService.ReadPageAsync(_index, pageNumber, token);
                    foreach (var line in page.Lines)
                    {
                        if (Matches(line, query, allowed))
                        {
                            matches.Add(line);
                            if (matches.Count == retainedCap) break;
                        }
                    }
                    LoadProgress.Value = (double)(pageNumber + 1) / _index.PageCount;
                }

                var suffix = matches.Count == retainedCap ? " Result cap reached." : "";
                ShowRows(matches, $"Found {matches.Count:N0} matching entries.{suffix}");
                PageControls.Visibility = Visibility.Collapsed;
                return;
            }

            var filtered = await Task.Run(
                () => Filter(_memoryLines, query, allowed, token),
                token);
            ShowRows(filtered, $"Showing {filtered.Count:N0} of {_memoryLines.Count:N0} entries");
        }
        catch (OperationCanceledException) { }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private HashSet<string> GetAllowedLevels()
    {
        var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (FilterTrace.IsChecked == true) levels.Add("TRACE");
        if (FilterDebug.IsChecked == true) levels.Add("DEBUG");
        if (FilterInfo.IsChecked == true) levels.Add("INFO");
        if (FilterWarn.IsChecked == true) levels.Add("WARN");
        if (FilterError.IsChecked == true) levels.Add("ERROR");
        if (FilterFatal.IsChecked == true) levels.Add("FATAL");
        return levels;
    }

    private static List<LogLine> Filter(
        IEnumerable<LogLine> source,
        string query,
        HashSet<string> allowed,
        CancellationToken cancellationToken)
    {
        var result = new List<LogLine>();
        foreach (var line in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Matches(line, query, allowed)) result.Add(line);
        }
        return result;
    }

    private static bool Matches(LogLine line, string query, HashSet<string> allowed)
    {
        if (!allowed.Contains(line.Level)) return false;
        return string.IsNullOrEmpty(query) ||
               line.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               line.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               line.Timestamp.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               line.Level.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static List<LogLine> ParseText(string text, int cap)
    {
        var result = new List<LogLine>(Math.Min(cap, 4096));
        using var reader = new StringReader(text);
        string? raw;
        long lineNumber = 0;
        while (result.Count < cap && (raw = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (LogLineParser.IsContinuation(raw) && result.Count > 0)
            {
                var previous = result[^1];
                result[^1] = previous with { Message = previous.Message + Environment.NewLine + raw.TrimEnd() };
            }
            else
            {
                result.Add(LogLineParser.Parse(raw, lineNumber));
            }
        }
        return result;
    }

    private async void OnTailClick(object sender, RoutedEventArgs e)
    {
        if (TailToggle.IsChecked == true)
        {
            if (_currentPath is null || _index is null)
            {
                TailToggle.IsChecked = false;
                return;
            }
            await StartTailAsync();
        }
        else
        {
            StopTail();
            if (_currentPath is not null)
                await LoadFileAsync(_currentPath);
        }
    }

    private async Task StartTailAsync()
    {
        if (_currentPath is null || _index is null) return;
        _tailCts?.Cancel();
        _tailCts?.Dispose();
        _tailCts = new CancellationTokenSource();
        StopWatcherOnly();
        if (_currentPage != _index.PageCount - 1)
            await ShowPageAsync(_index.PageCount - 1);
        _tailLines = (LogGrid.ItemsSource as IEnumerable<LogLine>)?.ToList() ?? [];
        _tailPosition = new FileInfo(_currentPath).Length;
        _tailRemainder = "";
        PageControls.Visibility = Visibility.Collapsed;

        var directory = Path.GetDirectoryName(_currentPath)!;
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_currentPath))
        {
            NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnTailFileChanged;
        _watcher.Created += OnTailFileChanged;
        _watcher.Renamed += OnTailFileChanged;
        _watcher.Error += OnTailWatcherError;

        StatusText.Text = "Tailing file. Waiting for appended lines...";
        await ReadTailAsync();
    }

    private void OnTailFileChanged(object sender, FileSystemEventArgs e)
    {
        _tailDelayCts?.Cancel();
        _tailDelayCts?.Dispose();
        _tailDelayCts = new CancellationTokenSource();
        var token = _tailDelayCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100, token);
                await Dispatcher.InvokeAsync(ReadTailAsync).Task.Unwrap();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnTailWatcherError(object sender, ErrorEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            TailToggle.IsChecked = false;
            StopTail();
            StatusText.Text = "Tail stopped because the file watcher failed. Reload to continue.";
        });

    private async Task ReadTailAsync()
    {
        if (_currentPath is null || _index is null || TailToggle.IsChecked != true) return;
        var token = _tailCts?.Token ?? CancellationToken.None;
        var entered = false;
        try
        {
            await _tailGate.WaitAsync(token);
            entered = true;
            var info = new FileInfo(_currentPath);
            if (!info.Exists) return;
            if (info.Length < _tailPosition)
            {
                TailToggle.IsChecked = false;
                StopTail();
                StatusText.Text = "File restarted or rotated. Choose Reload to rebuild the index.";
                return;
            }
            if (info.Length == _tailPosition) return;

            await using var stream = new FileStream(_currentPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Position = _tailPosition;
            using var reader = new StreamReader(stream, _index.Encoding, false, 64 * 1024, leaveOpen: true);
            var appended = await reader.ReadToEndAsync(token);
            _tailPosition = stream.Position;

            var combined = _tailRemainder + appended;
            var normalized = combined.ReplaceLineEndings("\n");
            var complete = normalized.EndsWith('\n');
            var parts = normalized.Split('\n');
            _tailRemainder = complete ? "" : parts[^1];
            var take = complete ? parts.Length - 1 : parts.Length - 1;
            var nextLine = _tailLines.Count == 0 ? _index.TotalLines + 1 : _tailLines[^1].LineNumber + 1;
            for (var index = 0; index < take; index++)
            {
                var raw = parts[index];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (LogLineParser.IsContinuation(raw) && _tailLines.Count > 0)
                {
                    var previous = _tailLines[^1];
                    _tailLines[^1] = previous with { Message = previous.Message + Environment.NewLine + raw.TrimEnd() };
                }
                else
                {
                    _tailLines.Add(LogLineParser.Parse(raw, nextLine++));
                }
            }

            var cap = Math.Clamp(AppSettings.Current?.LogViewerRetainedEntries ?? 100_000, 10_000, 500_000);
            if (_tailLines.Count > cap)
                _tailLines.RemoveRange(0, _tailLines.Count - cap);

            await ApplyFiltersAsync();
            StatusText.Text = $"Tailing file. Retaining {_tailLines.Count:N0} recent entries.";
        }
        catch (IOException exception)
        {
            StatusText.Text = $"Tail read delayed: {exception.Message}";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (entered) _tailGate.Release();
        }
    }

    private void ShowRows(IReadOnlyList<LogLine> rows, string status)
    {
        LogGrid.ItemsSource = rows;
        LogGrid.Visibility = Visibility.Visible;
        DropZone.Visibility = Visibility.Collapsed;
        UpdateCounts(rows);
        StatusText.Text = status;
    }

    private void UpdateCounts(IReadOnlyList<LogLine> rows)
    {
        var errors = rows.Count(line => line.Level is "ERROR" or "FATAL");
        var warnings = rows.Count(line => line.Level == "WARN");
        CountText.Text = errors > 0
            ? $"{errors:N0} errors | {warnings:N0} warnings"
            : warnings > 0 ? $"{warnings:N0} warnings" : $"{rows.Count:N0} rows";
    }

    private void ShowEmpty()
    {
        LogGrid.ItemsSource = null;
        LogGrid.Visibility = Visibility.Collapsed;
        DropZone.Visibility = Visibility.Visible;
        PageControls.Visibility = Visibility.Collapsed;
        CountText.Text = "";
    }

    private void ResetSession(bool clearPath)
    {
        _searchDelay.Stop();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        CancelFilter();
        StopTail();
        _index = null;
        _memoryLines = [];
        _tailLines = [];
        if (clearPath) _currentPath = null;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ReloadButton.IsEnabled = false;
        TailToggle.IsEnabled = false;
        TailToggle.IsChecked = false;
    }

    private void CancelFilter()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = null;
    }

    private void StopTail()
    {
        _tailCts?.Cancel();
        _tailCts?.Dispose();
        _tailCts = null;
        _tailDelayCts?.Cancel();
        _tailDelayCts?.Dispose();
        _tailDelayCts = null;
        StopWatcherOnly();
        _tailRemainder = "";
    }

    private void StopWatcherOnly()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnTailFileChanged;
        _watcher.Created -= OnTailFileChanged;
        _watcher.Renamed -= OnTailFileChanged;
        _watcher.Error -= OnTailWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    public Dictionary<string, string> SaveState() =>
        _currentPath is null ? [] : new() { ["path"] = _currentPath };

    public async void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("path", out var path) && File.Exists(path))
            await LoadFileAsync(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchDelay.Stop();
        _searchDelay.Tick -= OnSearchDelayTick;
        ResetSession(clearPath: true);
    }
}
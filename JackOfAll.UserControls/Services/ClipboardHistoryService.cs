using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using JackOfAll.Lib;
using JackOfAll.Lib.Settings;

namespace JackOfAll.UserControls.Services;

internal sealed class ClipboardHistoryService : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly string StorePath = Path.Combine(AppPaths.Root, "clipboard-history.json");

    private readonly List<ClipEntry> _clips = [];
    private readonly List<Subscription> _subscriptions = [];
    private readonly object _saveGate = new();
    private HwndSource? _source;
    private CancellationTokenSource? _saveCts;
    private bool _loaded;
    private int _changeVersion;
    private int _persistedVersion;
    private string _lastClipText = "";

    public IReadOnlyList<ClipEntry> Clips => _clips;
    public bool IsPaused { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    public IDisposable Subscribe(Action changed, Action<string> status)
    {
        if (!_loaded)
        {
            LoadFromDisk();
            _loaded = true;
        }

        var subscription = new Subscription(this, changed, status);
        _subscriptions.Add(subscription);
        if (_subscriptions.Count == 1)
            AttachListener();

        changed();
        return subscription;
    }

    public void TogglePaused()
    {
        IsPaused = !IsPaused;
        NotifyChanged();
        NotifyStatus(IsPaused ? "Monitoring paused." : "Monitoring clipboard.");
    }

    public void Copy(string text)
    {
        try
        {
            _lastClipText = text;
            Clipboard.SetDataObject(text, true);
            NotifyStatus("Copied to clipboard.");
        }
        catch
        {
            NotifyStatus("Failed to access clipboard.");
        }
    }

    public void TogglePin(ClipEntry clip)
    {
        var index = _clips.IndexOf(clip);
        if (index < 0) return;

        var toggled = clip with { IsPinned = !clip.IsPinned };
        _clips.RemoveAt(index);
        _clips.Insert(toggled.IsPinned ? 0 : GetInsertIndex(), toggled);
        TrimToLimits();
        MarkChanged();
    }

    public void Remove(ClipEntry clip)
    {
        if (!_clips.Remove(clip)) return;
        MarkChanged();
        NotifyStatus("Clipboard entry removed.");
    }

    public void ClearUnpinned()
    {
        _clips.RemoveAll(static clip => !clip.IsPinned);
        MarkChanged();
        NotifyStatus("History cleared. Pinned items preserved.");
    }

    private void AttachListener()
    {
        var window = Application.Current?.MainWindow;
        if (window is null) return;

        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        if (_source is null || !AddClipboardFormatListener(handle))
        {
            _source = null;
            NotifyStatus("Clipboard monitoring is unavailable.");
            return;
        }

        _source.AddHook(WndProc);
        _lastClipText = TryGetClipboardText();
    }

    private void DetachListener()
    {
        if (_source is null) return;
        _source.RemoveHook(WndProc);
        RemoveClipboardFormatListener(_source.Handle);
        _source = null;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate && !IsPaused)
            CaptureClipboard();
        return nint.Zero;
    }

    private void CaptureClipboard()
    {
        if (IsMarkedPrivate()) return;
        var text = TryGetClipboardText();
        if (string.IsNullOrEmpty(text) || text == _lastClipText) return;
        _lastClipText = text;

        var settings = AppSettings.Current;
        var maxEntryBytes = Math.Clamp(settings?.ClipboardMaxEntryKb ?? 256, 16, 4096) * 1024;
        var sizeBytes = Encoding.UTF8.GetByteCount(text);
        if (sizeBytes > maxEntryBytes)
        {
            NotifyStatus($"Clipboard item skipped: {FormatBytes(sizeBytes)} exceeds the {FormatBytes(maxEntryBytes)} item limit.");
            return;
        }

        var existing = _clips.FirstOrDefault(clip => clip.Content == text);
        if (existing is not null)
            _clips.Remove(existing);

        _clips.Insert(GetInsertIndex(), new ClipEntry(text, DateTime.Now, existing?.IsPinned ?? false));
        if (!TrimToLimits())
        {
            NotifyStatus("Clipboard history is full because pinned items consume the configured storage limit.");
        }
        MarkChanged();
    }

    private bool TrimToLimits()
    {
        var settings = AppSettings.Current;
        var maxEntries = Math.Clamp(settings?.ClipboardMaxEntries ?? 100, 10, 500);
        var maxPayloadBytes = (long)Math.Clamp(settings?.ClipboardStorageLimitMb ?? 5, 1, 100) * 1024 * 1024;

        while (_clips.Count > maxEntries || PayloadBytes() > maxPayloadBytes)
        {
            var removeIndex = _clips.FindLastIndex(static clip => !clip.IsPinned);
            if (removeIndex < 0) return false;
            _clips.RemoveAt(removeIndex);
        }
        return true;
    }

    private long PayloadBytes()
    {
        long total = 0;
        foreach (var clip in _clips)
            total += Encoding.UTF8.GetByteCount(clip.Content);
        return total;
    }

    private int GetInsertIndex()
    {
        var index = _clips.FindIndex(static clip => !clip.IsPinned);
        return index < 0 ? _clips.Count : index;
    }

    private void MarkChanged()
    {
        _changeVersion++;
        NotifyChanged();
        ScheduleSave();
    }

    private void NotifyChanged()
    {
        foreach (var subscription in _subscriptions.ToArray())
            subscription.Changed();
    }

    private void NotifyStatus(string message)
    {
        foreach (var subscription in _subscriptions.ToArray())
            subscription.Status(message);
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        var snapshot = CreateSnapshot();
        var version = _changeVersion;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token).ConfigureAwait(false);
                Persist(snapshot, version);
            }
            catch (OperationCanceledException) { }
            catch
            {
                Application.Current?.Dispatcher.BeginInvoke(
                    () => NotifyStatus("Clipboard history could not be saved."));
            }
        });
    }

    private List<ClipDto> CreateSnapshot() =>
        _clips.Select(static clip => new ClipDto(clip.Content, clip.CopiedAt, clip.IsPinned)).ToList();

    private void Persist(List<ClipDto> snapshot, int version)
    {
        lock (_saveGate)
        {
            if (version < _persistedVersion) return;
            AppPaths.EnsureRoot();
            var tempPath = StorePath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, StorePath, true);
            _persistedVersion = version;
        }
    }

    private void Flush()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;
        if (_persistedVersion == _changeVersion) return;
        try { Persist(CreateSnapshot(), _changeVersion); }
        catch { NotifyStatus("Clipboard history could not be saved."); }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var data = JsonSerializer.Deserialize<List<ClipDto>>(File.ReadAllText(StorePath));
            if (data is null) return;
            foreach (var item in data)
                _clips.Add(new ClipEntry(item.Content, item.CopiedAt, item.IsPinned));
            TrimToLimits();
        }
        catch
        {
            try
            {
                var recovery = StorePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(StorePath, recovery, false);
            }
            catch { }
        }
    }

    /// <summary>
    /// Password managers (and the Connectivity tool's Wi-Fi password copy) add this format to ask clipboard
    /// history tools, Windows' own included, not to record the clip.
    /// </summary>
    public const string ExcludeFromHistoryFormat = "ExcludeClipboardContentFromMonitorProcessing";

    private static bool IsMarkedPrivate()
    {
        try { return Clipboard.ContainsData(ExcludeFromHistoryFormat); }
        catch { return false; }
    }

    private static string TryGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch { return ""; }
    }

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:F0} KB"
        : $"{bytes / 1024.0 / 1024.0:F1} MB";

    private void Unsubscribe(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        if (_subscriptions.Count != 0) return;
        DetachListener();
        Flush();
    }

    public void Dispose()
    {
        DetachListener();
        Flush();
        _subscriptions.Clear();
    }

    private sealed class Subscription(
        ClipboardHistoryService owner,
        Action changed,
        Action<string> status) : IDisposable
    {
        private ClipboardHistoryService? _owner = owner;
        public Action Changed { get; } = changed;
        public Action<string> Status { get; } = status;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            current?.Unsubscribe(this);
        }
    }

    private sealed record ClipDto(string Content, DateTime CopiedAt, bool IsPinned);
}

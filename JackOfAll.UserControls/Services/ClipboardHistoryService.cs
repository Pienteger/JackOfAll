using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JackOfAll.Lib;
using JackOfAll.Lib.Settings;
using static JackOfAll.UserControls.Services.ClipboardNative;

namespace JackOfAll.UserControls.Services;

/// <summary>
/// Clipboard history for the whole app. It records text (with its HTML/RTF formatting), images and copied files,
/// reads the text inside images, skips copies that password managers mark as private, and owns the global
/// quick-picker shortcut. It records while JackOfAll runs, or only while a Clipboard tab is open if background
/// recording is turned off in Settings.
/// </summary>
internal sealed class ClipboardHistoryService : IDisposable
{
    private const int HotkeyId = 0x4A01;
    private const int MaxFormattingBytes = 1 << 20;
    private static readonly string StorePath = Path.Combine(AppPaths.Root, "clipboard-history.json");
    internal static readonly string ImageDir = Path.Combine(AppPaths.Root, "clipboard-images");

    private static readonly (string Name, uint Modifiers)[] Shortcuts =
    [
        ("Ctrl+Shift+V", ModControl | ModShift),
        ("Ctrl+Alt+V", ModControl | ModAlt),
    ];

    private static readonly string[] PasswordApps =
        ["keepass", "1password", "bitwarden", "lastpass", "dashlane", "enpass", "nordpass", "protonpass", "roboform", "keeper"];

    private readonly List<ClipEntry> _clips = [];
    private readonly List<Subscription> _subscriptions = [];
    private readonly object _saveGate = new();
    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _pasteTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private HwndSource? _source;
    private ClipboardOcr? _ocr;
    private ClipboardQuickPicker? _picker;
    private CancellationTokenSource? _saveCts;
    private AppSettings? _settings;
    private bool _loaded;
    private bool _listening;
    private bool _hotkeyRegistered;
    private int _changeVersion;
    private int _persistedVersion;
    private int _readTries;
    private uint _ignoreSequence;

    public IReadOnlyList<ClipEntry> Clips => _clips;
    public bool IsPaused { get; private set; }
    public bool IsRecording => _listening && !IsPaused;
    public bool RecordsInBackground => _settings?.ClipboardRecordInBackground ?? false;

    /// <summary>The quick picker's shortcut, or "" when it is off or another app holds every option.</summary>
    public string Shortcut { get; private set; } = "";

    public ClipboardHistoryService()
    {
        _readTimer.Tick += (_, _) => { _readTimer.Stop(); ReadClipboard(); };
        _pasteTimer.Tick += (_, _) => { _pasteTimer.Stop(); SendPaste(); };
    }

    // ── lifetime ───────────────────────────────────────────────

    /// <summary>Called once the main window exists: starts background recording and the shortcut, as Settings allow.</summary>
    public void Start(Window mainWindow, AppSettings settings)
    {
        _settings = settings;
        EnsureLoaded();
        var handle = new WindowInteropHelper(mainWindow).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        settings.PropertyChanged += OnSettingsChanged;
        Application.Current.Exit += (_, _) => Dispose();
        UpdateListener();
        UpdateShortcut();
    }

    public IDisposable Subscribe(Action changed, Action<string> status)
    {
        EnsureLoaded();
        var subscription = new Subscription(this, changed, status);
        _subscriptions.Add(subscription);
        UpdateListener();
        changed();
        return subscription;
    }

    private void Unsubscribe(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        UpdateListener();
        if (_subscriptions.Count == 0) Flush();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _ocr = new ClipboardOcr(Dispatcher.CurrentDispatcher, OnOcrDone);
        LoadFromDisk();
        foreach (var clip in _clips)
            if (clip.IsImage && !clip.OcrDone) _ocr.Enqueue(clip);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ClipboardRecordInBackground)) UpdateListener();
        else if (e.PropertyName == nameof(AppSettings.ClipboardQuickPicker)) UpdateShortcut();
        else if (e.PropertyName is nameof(AppSettings.ClipboardImageStorageMb) or nameof(AppSettings.ClipboardMaxEntries)
                 or nameof(AppSettings.ClipboardStorageLimitMb))
        {
            TrimToLimits();
            MarkChanged();
        }
    }

    private void UpdateListener()
    {
        var want = _source is not null && (RecordsInBackground || _subscriptions.Count > 0);
        if (want == _listening) { NotifyChanged(); return; }
        if (want)
        {
            if (!AddClipboardFormatListener(_source!.Handle))
            {
                NotifyStatus("Clipboard monitoring is unavailable.");
                return;
            }
            _listening = true;
        }
        else
        {
            RemoveClipboardFormatListener(_source!.Handle);
            _listening = false;
        }
        NotifyChanged();
    }

    private void UpdateShortcut()
    {
        if (_source is null) return;
        if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
        _hotkeyRegistered = false;
        Shortcut = "";
        if (_settings?.ClipboardQuickPicker == true)
        {
            foreach (var (name, modifiers) in Shortcuts)
            {
                if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, VkV)) continue;
                _hotkeyRegistered = true;
                Shortcut = name;
                break;
            }
            if (!_hotkeyRegistered)
                NotifyStatus("Another app is using Ctrl+Shift+V and Ctrl+Alt+V, so the quick picker has no shortcut.");
        }
        NotifyChanged();
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate && _listening)
        {
            // Apps often put several formats on the clipboard in quick succession; read once they're done.
            _readTries = 0;
            _readTimer.Stop();
            _readTimer.Start();
        }
        else if (message == WmHotkey && wParam == HotkeyId)
        {
            ShowQuickPicker(GetForegroundWindow());
            handled = true;
        }
        return nint.Zero;
    }

    public void ShowQuickPicker(nint target)
    {
        EnsureLoaded();
        _picker ??= new ClipboardQuickPicker(this);
        if (_picker.IsVisible) { _picker.Hide(); return; }
        _picker.Open(target);
    }

    public void TogglePaused()
    {
        IsPaused = !IsPaused;
        NotifyChanged();
        NotifyStatus(IsPaused ? "Recording paused." : "Recording the clipboard.");
    }

    // ── capture ────────────────────────────────────────────────

    private void ReadClipboard()
    {
        var sequence = GetClipboardSequenceNumber();
        if (IsPaused || sequence == _ignoreSequence) return;

        IDataObject? data;
        try
        {
            data = Clipboard.GetDataObject();
        }
        catch (COMException)
        {
            // Another app still has the clipboard open; try again shortly.
            if (++_readTries < 5) _readTimer.Start();
            return;
        }
        if (data is null || IsPrivate(data)) return;

        var source = ClipboardSourceApp(out var process);
        if (PasswordApps.Any(process.Contains)) return;

        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                Add(new ClipEntry { Kind = ClipKind.Files, Content = string.Join("\n", files), Files = files, Source = source });
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
                CaptureText(data, text, source);
            else
                CaptureImage(data, source);
        }
        catch (COMException) { }
        catch (OutOfMemoryException) { NotifyStatus("Clipboard item skipped: it was too large to read."); }
    }

    // Password managers mark their copies with these formats; Windows' own clipboard history skips them too.
    private static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore"))
            return true;
        return data.GetDataPresent("CanIncludeInClipboardHistory")
            && data.GetData("CanIncludeInClipboardHistory") is MemoryStream { Length: >= 4 } flag
            && BitConverter.ToInt32(flag.ToArray(), 0) == 0;
    }

    private void CaptureText(IDataObject data, string text, string source)
    {
        var maxEntryBytes = Math.Clamp(_settings?.ClipboardMaxEntryKb ?? AppSettings.Current?.ClipboardMaxEntryKb ?? 256, 16, 4096) * 1024;
        var sizeBytes = Encoding.UTF8.GetByteCount(text);
        if (sizeBytes > maxEntryBytes)
        {
            NotifyStatus($"Clipboard item skipped: {FormatBytes(sizeBytes)} exceeds the {FormatBytes(maxEntryBytes)} item limit.");
            return;
        }

        var clip = new ClipEntry { Kind = ClipKind.Text, Content = text, Source = source };
        // Formatting is kept so a paste into Word or an email looks like the original, as long as it fits the item limit too.
        var html = TryGetString(data, DataFormats.Html);
        var rtf = TryGetString(data, DataFormats.Rtf);
        if (sizeBytes + html.Length + rtf.Length <= Math.Max(maxEntryBytes, MaxFormattingBytes))
        {
            clip.Html = html;
            clip.Rtf = rtf;
        }
        Add(clip);
    }

    private void CaptureImage(IDataObject data, string source)
    {
        if ((_settings?.ClipboardImageStorageMb ?? 0) <= 0) return;
        var decoded = ReadImage(data);
        if (decoded is null) return;

        // A decoded frame stays tied to this thread even when frozen, so copy its pixels before encoding elsewhere.
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        image.Freeze();

        _ = Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var bytes = stream.ToArray();
            // Named by content, so copying the same picture twice keeps one file.
            var id = Convert.ToHexString(SHA1.HashData(bytes), 0, 10).ToLowerInvariant();
            Directory.CreateDirectory(ImageDir);
            var path = Path.Combine(ImageDir, id + ".png");
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            return id;
        }).ContinueWith(task =>
        {
            if (task.IsFaulted) { NotifyStatus("The copied image could not be saved."); return; }
            var clip = new ClipEntry { Kind = ClipKind.Image, ImageId = task.Result, Width = image.PixelWidth, Height = image.PixelHeight, Source = source };
            if (Add(clip) == clip) _ocr?.Enqueue(clip);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static BitmapSource? ReadImage(IDataObject data)
    {
        // Prefer PNG (keeps transparency, used by Snipping Tool and browsers), then the DIB that every app provides.
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
                return BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch { }
        try
        {
            if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
                return DibToBitmap(dib.ToArray());
        }
        catch { }
        try { return Clipboard.ContainsImage() ? Clipboard.GetImage() : null; }
        catch { return null; }
    }

    // WPF's own Clipboard.GetImage often returns a fully transparent bitmap for 32-bit screenshots, so the DIB
    // is wrapped in a BMP file header and decoded directly (the BMP decoder ignores the bogus alpha channel).
    private static BitmapSource? DibToBitmap(byte[] dib)
    {
        if (dib.Length < 40) return null;
        var headerSize = BitConverter.ToInt32(dib, 0);
        var bitCount = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var colorsUsed = BitConverter.ToInt32(dib, 32);
        var masks = compression == 3 && headerSize == 40 ? 12 : 0;
        var palette = (bitCount <= 8 ? (colorsUsed == 0 ? 1 << bitCount : colorsUsed) : colorsUsed) * 4;
        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(14 + headerSize + masks + palette).CopyTo(file, 10);
        dib.CopyTo(file, 14);
        return BitmapFrame.Create(new MemoryStream(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static string TryGetString(IDataObject data, string format)
    {
        try { return data.GetDataPresent(format) && data.GetData(format) is string value && value.Length < MaxFormattingBytes ? value : ""; }
        catch { return ""; }
    }

    private void OnOcrDone(ClipEntry clip, string text)
    {
        clip.OcrText = text;
        clip.OcrDone = true;
        MarkChanged();
    }

    /// <summary>Adds a clip, or moves an identical older one to the top. Returns the clip that is now in the history.</summary>
    private ClipEntry Add(ClipEntry clip)
    {
        var existing = _clips.FirstOrDefault(other => SameContent(other, clip));
        if (existing is not null)
        {
            _clips.Remove(existing);
            existing.CopiedAt = DateTime.Now;
            if (clip.Source.Length > 0) existing.Source = clip.Source;
            if (clip.HasFormatting) { existing.Html = clip.Html; existing.Rtf = clip.Rtf; }
            _clips.Insert(GetInsertIndex(), existing);
            MarkChanged();
            return existing;
        }

        clip.CopiedAt = DateTime.Now;
        _clips.Insert(GetInsertIndex(), clip);
        if (!TrimToLimits())
            NotifyStatus("Clipboard history is full because pinned items consume the configured storage limit.");
        MarkChanged();
        return clip;
    }

    private static bool SameContent(ClipEntry a, ClipEntry b) => a.Kind == b.Kind && a.Kind switch
    {
        ClipKind.Image => a.ImageId == b.ImageId,
        _ => a.Content == b.Content,
    };

    // ── using clips ────────────────────────────────────────────

    public IEnumerable<ClipEntry> Search(string query, ClipFilter filter)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _clips.Where(clip => clip.Matches(filter) && words.All(word => clip.Haystack.Contains(word, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Puts a clip back on the clipboard. "Plain" drops text formatting, pastes file paths as text, and pastes
    /// the text read from an image instead of the image.
    /// </summary>
    public bool Copy(ClipEntry clip, bool plain = false)
    {
        var data = new DataObject();
        try
        {
            switch (clip.Kind)
            {
                case ClipKind.Text:
                    data.SetText(clip.Content, TextDataFormat.UnicodeText);
                    if (!plain && clip.Html.Length > 0) data.SetData(DataFormats.Html, clip.Html);
                    if (!plain && clip.Rtf.Length > 0) data.SetData(DataFormats.Rtf, clip.Rtf);
                    break;
                case ClipKind.Files when plain:
                    data.SetText(string.Join("\r\n", clip.Files), TextDataFormat.UnicodeText);
                    break;
                case ClipKind.Files:
                    var files = new StringCollection();
                    files.AddRange(clip.Files);
                    data.SetFileDropList(files);
                    break;
                case ClipKind.Image when plain:
                    if (clip.OcrText.Length == 0)
                    {
                        NotifyStatus(clip.OcrDone ? "No text was found in that image." : "Still reading the text in that image.");
                        return false;
                    }
                    data.SetText(clip.OcrText, TextDataFormat.UnicodeText);
                    break;
                case ClipKind.Image:
                    var bytes = File.ReadAllBytes(clip.ImagePath);
                    data.SetImage(BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
                    data.SetData("PNG", new MemoryStream(bytes));
                    break;
            }
            if (!SetClipboard(data)) return false;
        }
        catch (IOException)
        {
            NotifyStatus("That image is no longer on disk.");
            return false;
        }

        clip.Uses++;
        if (!clip.IsPinned)
        {
            _clips.Remove(clip);
            clip.CopiedAt = DateTime.Now;
            _clips.Insert(GetInsertIndex(), clip);
        }
        MarkChanged();
        NotifyStatus(plain && clip.IsImage ? "Text from the image copied." : "Copied to clipboard.");
        return true;
    }

    public void CopyText(string text)
    {
        if (SetClipboard(new DataObject(DataFormats.UnicodeText, text))) NotifyStatus("Copied to clipboard.");
    }

    /// <summary>Copies a clip and pastes it into the window that was in front before the quick picker opened.</summary>
    public void PasteInto(ClipEntry clip, bool plain, nint target)
    {
        if (!Copy(clip, plain)) return;
        if (target == nint.Zero || !IsWindow(target)) return;
        SetForegroundWindow(target);
        _pasteTimer.Start();
    }

    private bool SetClipboard(DataObject data)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                _ignoreSequence = GetClipboardSequenceNumber();
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        NotifyStatus("Failed to access clipboard.");
        return false;
    }

    public void TogglePin(ClipEntry clip)
    {
        if (!_clips.Remove(clip)) return;
        clip.IsPinned = !clip.IsPinned;
        _clips.Insert(clip.IsPinned ? 0 : GetInsertIndex(), clip);
        TrimToLimits();
        MarkChanged();
    }

    public void Remove(ClipEntry clip)
    {
        if (!_clips.Remove(clip)) return;
        DeleteImage(clip);
        MarkChanged();
        NotifyStatus("Clipboard entry removed.");
    }

    public void ClearUnpinned()
    {
        foreach (var clip in _clips.Where(static clip => !clip.IsPinned).ToList())
        {
            _clips.Remove(clip);
            DeleteImage(clip);
        }
        MarkChanged();
        NotifyStatus("History cleared. Pinned items preserved.");
    }

    private void DeleteImage(ClipEntry clip)
    {
        if (!clip.IsImage || _clips.Any(other => other.ImageId == clip.ImageId)) return;
        try { File.Delete(clip.ImagePath); } catch { }
    }

    // ── limits ─────────────────────────────────────────────────

    private bool TrimToLimits()
    {
        var settings = _settings ?? AppSettings.Current;
        var maxEntries = Math.Clamp(settings?.ClipboardMaxEntries ?? 100, 10, 500);
        var maxPayloadBytes = (long)Math.Clamp(settings?.ClipboardStorageLimitMb ?? 5, 1, 100) * 1024 * 1024;
        var maxImageBytes = (long)Math.Clamp(settings?.ClipboardImageStorageMb ?? 300, 0, 2000) * 1024 * 1024;

        while (_clips.Count > maxEntries || PayloadBytes() > maxPayloadBytes)
        {
            var removeIndex = _clips.FindLastIndex(static clip => !clip.IsPinned);
            if (removeIndex < 0) return false;
            RemoveAt(removeIndex);
        }
        while (ImageBytes() > maxImageBytes)
        {
            var removeIndex = _clips.FindLastIndex(static clip => !clip.IsPinned && clip.IsImage);
            if (removeIndex < 0) return false;
            RemoveAt(removeIndex);
        }
        return true;
    }

    private void RemoveAt(int index)
    {
        var clip = _clips[index];
        _clips.RemoveAt(index);
        DeleteImage(clip);
    }

    // Images are counted separately (see ImageBytes), so a few screenshots don't push all the text out.
    private long PayloadBytes()
    {
        long total = 0;
        foreach (var clip in _clips)
            total += Encoding.UTF8.GetByteCount(clip.Content) + clip.Html.Length + clip.Rtf.Length + Encoding.UTF8.GetByteCount(clip.OcrText);
        return total;
    }

    private long ImageBytes()
    {
        long total = 0;
        foreach (var clip in _clips)
        {
            if (!clip.IsImage) continue;
            try { total += new FileInfo(clip.ImagePath).Length; } catch { }
        }
        return total;
    }

    private int GetInsertIndex()
    {
        var index = _clips.FindIndex(static clip => !clip.IsPinned);
        return index < 0 ? _clips.Count : index;
    }

    // ── change notification and persistence ────────────────────

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
        _picker?.Refresh();
    }

    private void NotifyStatus(string message)
    {
        foreach (var subscription in _subscriptions.ToArray())
            subscription.Status(message);
        _picker?.ShowStatus(message);
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

    private List<ClipDto> CreateSnapshot() => _clips.Select(static clip => new ClipDto
    {
        Content = clip.Content,
        CopiedAt = clip.CopiedAt,
        IsPinned = clip.IsPinned,
        Kind = clip.Kind.ToString(),
        Html = clip.Html.Length > 0 ? clip.Html : null,
        Rtf = clip.Rtf.Length > 0 ? clip.Rtf : null,
        ImageId = clip.ImageId.Length > 0 ? clip.ImageId : null,
        Width = clip.Width,
        Height = clip.Height,
        Source = clip.Source.Length > 0 ? clip.Source : null,
        OcrText = clip.OcrText.Length > 0 ? clip.OcrText : null,
        OcrDone = clip.OcrDone,
        Uses = clip.Uses,
    }).ToList();

    private void Persist(List<ClipDto> snapshot, int version)
    {
        lock (_saveGate)
        {
            if (version < _persistedVersion) return;
            AppPaths.EnsureRoot();
            var tempPath = StorePath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
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
            {
                var kind = Enum.TryParse<ClipKind>(item.Kind, out var parsed) ? parsed : ClipKind.Text;
                var clip = new ClipEntry
                {
                    Kind = kind,
                    Content = item.Content ?? "",
                    Html = item.Html ?? "",
                    Rtf = item.Rtf ?? "",
                    ImageId = item.ImageId ?? "",
                    Width = item.Width,
                    Height = item.Height,
                    Files = kind == ClipKind.Files ? (item.Content ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries) : [],
                    CopiedAt = item.CopiedAt,
                    IsPinned = item.IsPinned,
                    Source = item.Source ?? "",
                    OcrText = item.OcrText ?? "",
                    OcrDone = item.OcrDone,
                    Uses = item.Uses,
                };
                if (clip.IsImage && !File.Exists(clip.ImagePath)) continue;
                _clips.Add(clip);
            }
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

    private static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:F0} KB"
        : $"{bytes / 1024.0 / 1024.0:F1} MB";

    public void Dispose()
    {
        if (_source is not null)
        {
            if (_listening) RemoveClipboardFormatListener(_source.Handle);
            if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _listening = false;
            _hotkeyRegistered = false;
        }
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

    // Older history files only have Content, CopiedAt and IsPinned; every other field is optional.
    private sealed class ClipDto
    {
        public string? Content { get; set; }
        public DateTime CopiedAt { get; set; }
        public bool IsPinned { get; set; }
        public string? Kind { get; set; }
        public string? Html { get; set; }
        public string? Rtf { get; set; }
        public string? ImageId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? Source { get; set; }
        public string? OcrText { get; set; }
        public bool OcrDone { get; set; }
        public int Uses { get; set; }
    }
}

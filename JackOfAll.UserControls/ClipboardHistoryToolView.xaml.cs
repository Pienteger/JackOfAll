using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls;

public partial class ClipboardHistoryToolView : UserControl, IStatefulTool, IDisposable
{
    private readonly ClipboardHistoryService _service;
    private readonly ObservableCollection<ClipEntry> _filtered = [];
    private readonly IDisposable _subscription;
    private ClipFilter _filter = ClipFilter.All;

    internal ClipboardHistoryToolView(ClipboardHistoryService service)
    {
        InitializeComponent();
        _service = service;
        ClipList.ItemsSource = _filtered;
        _subscription = service.Subscribe(RefreshFiltered, SetStatus);

        SearchBox.GotFocus += (_, _) => SearchPlaceholder.Visibility = Visibility.Collapsed;
        SearchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                SearchPlaceholder.Visibility = Visibility.Visible;
        };
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsFocused
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshFiltered();
    }

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: ClipFilter filter })
        {
            _filter = filter;
            if (IsInitialized && _service is not null) RefreshFiltered();
        }
    }

    private void RefreshFiltered()
    {
        var selected = ClipList.SelectedItem as ClipEntry;
        var query = SearchBox?.Text?.Trim() ?? "";
        var items = _service.Search(query, _filter).ToList();
        // Rows update themselves when a clip changes (e.g. OCR finishes); only rebuild when the set or order differs,
        // so the list keeps its scroll position.
        if (!items.SequenceEqual(_filtered))
        {
            _filtered.Clear();
            foreach (var clip in items) _filtered.Add(clip);
        }
        if (selected is not null && items.Contains(selected)) ClipList.SelectedItem = selected;
        else if (items.Count > 0) ClipList.SelectedIndex = 0;
        Preview.Entry = ClipList.SelectedItem as ClipEntry;

        var total = _service.Clips.Count;
        var pinned = _service.Clips.Count(static clip => clip.IsPinned);
        CountText.Text = pinned > 0 ? $"{total} clips | {pinned} pinned" : $"{total} clips";

        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = total == 0 ? "Nothing copied yet. Copy some text, a screenshot or files and they show up here."
            : query.Length > 0 ? $"Nothing matches “{query}”. Search also looks inside images and at file names."
            : _filter == ClipFilter.Pinned ? "Nothing pinned yet. Pinned items are never trimmed from history."
            : "Nothing of this kind yet.";

        PauseIcon.Text = _service.IsPaused ? "" : "";
        PauseLabel.Text = _service.IsPaused ? "Resume" : "Pause";
        MonitorDot.Fill = !_service.IsRecording
            ? (TryFindResource("WarningBrush") as Brush ?? Brushes.Orange)
            : (TryFindResource("SuccessText") as Brush ?? Brushes.Green);
        BtnPicker.ToolTip = _service.Shortcut.Length > 0
            ? $"Search and paste into any app ({_service.Shortcut})"
            : "Search and copy (the shortcut is off in Settings)";
        HintText.Text = _service.Shortcut.Length > 0
            ? $"{_service.Shortcut} opens the quick picker from any app"
            : _service.RecordsInBackground ? "" : "Records only while this tab is open (Settings › Clipboard)";
    }

    private ClipEntry? Selected => ClipList.SelectedItem as ClipEntry;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Preview.Entry = Selected;

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up && ClipList.Items.Count > 0)
        {
            var step = e.Key == Key.Down ? 1 : -1;
            ClipList.SelectedIndex = Math.Clamp(ClipList.SelectedIndex + step, 0, ClipList.Items.Count - 1);
            ClipList.ScrollIntoView(ClipList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Selected is { } clip)
        {
            _service.Copy(clip, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { } clip) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Enter: _service.Copy(clip, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); break;
            case Key.Delete: _service.Remove(clip); break;
            case Key.P when ctrl: _service.TogglePin(clip); break;
            case Key.C when ctrl: _service.Copy(clip); break;
            case Key.T when ctrl && clip.IsImage: _service.Copy(clip, plain: true); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipEntry clip })
            _service.Copy(clip);
    }

    private void OnClipDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } clip)
            _service.Copy(clip);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipEntry clip })
            _service.TogglePin(clip);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipEntry clip })
            _service.Remove(clip);
    }

    // ── context menu ───────────────────────────────────────────

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var clip = Selected;
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>()) item.IsEnabled = clip is not null;
        if (clip is null) return;
        MenuCopyPlain.Header = clip.Kind switch
        {
            ClipKind.Image => "Copy the text in the image",
            ClipKind.Files => "Copy the file paths",
            _ => "Copy as plain text",
        };
        MenuCopyPlain.InputGestureText = clip.IsImage ? "Ctrl+T" : "Shift+Enter";
        MenuCopyPlain.Visibility = clip.IsText && !clip.HasFormatting ? Visibility.Collapsed : Visibility.Visible;
        MenuShowFolder.Visibility = clip.IsFiles ? Visibility.Visible : Visibility.Collapsed;
        MenuOpenImage.Visibility = clip.IsImage ? Visibility.Visible : Visibility.Collapsed;
        MenuPin.Header = clip.IsPinned ? "Unpin" : "Pin";
    }

    private void OnMenuCopy(object sender, RoutedEventArgs e) { if (Selected is { } clip) _service.Copy(clip); }
    private void OnMenuCopyPlain(object sender, RoutedEventArgs e) { if (Selected is { } clip) _service.Copy(clip, plain: true); }
    private void OnMenuPin(object sender, RoutedEventArgs e) { if (Selected is { } clip) _service.TogglePin(clip); }
    private void OnMenuDelete(object sender, RoutedEventArgs e) { if (Selected is { } clip) _service.Remove(clip); }

    private void OnMenuShowFolder(object sender, RoutedEventArgs e)
    {
        if (Selected is { IsFiles: true } clip && clip.Files.Length > 0)
            StartShell("explorer.exe", $"/select,\"{clip.Files[0]}\"");
    }

    private void OnMenuOpenImage(object sender, RoutedEventArgs e)
    {
        if (Selected is { IsImage: true } clip)
            StartShell(clip.ImagePath, null);
    }

    private void StartShell(string file, string? arguments)
    {
        try { Process.Start(new ProcessStartInfo(file) { Arguments = arguments ?? "", UseShellExecute = true }); }
        catch (Exception exception) { SetStatus($"Couldn't open it: {exception.Message}"); }
    }

    // ── header buttons ─────────────────────────────────────────

    private void OnPickerClick(object sender, RoutedEventArgs e) => _service.ShowQuickPicker(nint.Zero);

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear all unpinned clipboard entries?\n\nPinned entries will be preserved.",
            "Clear Clipboard History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _service.ClearUnpinned();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e) => _service.TogglePaused();

    private void SetStatus(string message) => StatusText.Text = message;

    public Dictionary<string, string> SaveState() => [];
    public void LoadState(Dictionary<string, string> state) { }
    public void Dispose() => _subscription.Dispose();
}

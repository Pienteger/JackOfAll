using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls;

/// <summary>
/// Floating search-and-paste window opened by the global shortcut (Ctrl+Shift+V). Enter pastes the selected clip
/// into the app that was in front; it hides as soon as it loses focus.
/// </summary>
internal partial class ClipboardQuickPicker : Window
{
    private const string Keys = "Enter  paste      Shift+Enter  plain text      Ctrl+P  pin      Del  delete      Ctrl+T  copy text in image      Esc  close";

    private readonly ClipboardHistoryService _service;
    private readonly ObservableCollection<ClipEntry> _items = [];
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ClipFilter _filter = ClipFilter.All;
    private nint _target;
    private bool _jumpToFirst;

    public ClipboardQuickPicker(ClipboardHistoryService service)
    {
        InitializeComponent();
        _service = service;
        ClipList.ItemsSource = _items;
        HintText.Text = Keys;
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); HintText.Text = Keys; };
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += OnKey;
    }

    /// <param name="target">The window to paste into, or zero to only copy (when opened from inside JackOfAll).</param>
    public void Open(nint target)
    {
        _target = target;
        SearchBox.Text = "";
        ((RadioButton)FilterBar.Children[0]).IsChecked = true;
        _jumpToFirst = true;
        Refresh();
        HintText.Text = target == nint.Zero ? Keys.Replace("paste", "copy") : Keys;

        Show();
        CenterOnCursorMonitor();
        Activate();
        ClipboardNative.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        SearchBox.Focus();
    }

    // Positions in physical pixels so it lands on the monitor the user is working on, at any DPI.
    private void CenterOnCursorMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var work = ClipboardNative.WorkAreaAtCursor();
        ClipboardNative.GetWindowRect(hwnd, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var x = work.Left + Math.Max(0, (work.Right - work.Left - width) / 2);
        var y = work.Top + Math.Max(0, (work.Bottom - work.Top - height) / 3);
        ClipboardNative.SetWindowPos(hwnd, nint.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
    }

    public void Refresh()
    {
        if (!IsVisible && !_jumpToFirst) return;
        var selected = ClipList.SelectedItem as ClipEntry;
        var items = _service.Search(SearchBox.Text.Trim(), _filter).ToList();
        if (!items.SequenceEqual(_items))
        {
            _items.Clear();
            foreach (var clip in items) _items.Add(clip);
        }
        if (_jumpToFirst || selected is null || !items.Contains(selected)) ClipList.SelectedIndex = items.Count > 0 ? 0 : -1;
        else ClipList.SelectedItem = selected;
        _jumpToFirst = false;
        if (ClipList.SelectedItem is not null) ClipList.ScrollIntoView(ClipList.SelectedItem);
        Preview.Entry = ClipList.SelectedItem as ClipEntry;

        CountText.Text = _service.IsPaused ? "Recording paused" : $"{items.Count} of {_service.Clips.Count}";
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _service.Clips.Count == 0 ? "Nothing copied yet. Copy some text, a screenshot or files and they show up here."
            : SearchBox.Text.Length > 0 ? $"Nothing matches “{SearchBox.Text.Trim()}”."
            : "Nothing here yet.";
    }

    public void ShowStatus(string message)
    {
        if (!IsVisible) return;
        HintText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _jumpToFirst = true;
        Refresh();
    }

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: ClipFilter filter }) return;
        _filter = filter;
        _jumpToFirst = true;
        if (_service is not null) Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Preview.Entry = ClipList.SelectedItem as ClipEntry;

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => Paste(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var clip = ClipList.SelectedItem as ClipEntry;
        switch (e.Key)
        {
            case Key.Down: Move(1); break;
            case Key.Up: Move(-1); break;
            case Key.PageDown: Move(6); break;
            case Key.PageUp: Move(-6); break;
            case Key.Enter: Paste(shift); break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";
                else Hide();
                break;
            case Key.Tab:
                var chips = FilterBar.Children.OfType<RadioButton>().ToList();
                var next = (chips.FindIndex(static chip => chip.IsChecked == true) + (shift ? chips.Count - 1 : 1)) % chips.Count;
                chips[next].IsChecked = true;
                break;
            case Key.Delete when clip is not null && (ctrl || SearchBox.Text.Length == 0):
                var index = ClipList.SelectedIndex;
                _service.Remove(clip);
                if (_items.Count > 0) ClipList.SelectedIndex = Math.Min(index, _items.Count - 1);
                break;
            case Key.P when ctrl && clip is not null: _service.TogglePin(clip); break;
            case Key.T when ctrl && clip is { IsImage: true }: _service.Copy(clip, plain: true); break;
            case Key.C when ctrl && clip is not null && SearchBox.SelectionLength == 0: _service.Copy(clip); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int step)
    {
        if (_items.Count == 0) return;
        ClipList.SelectedIndex = Math.Clamp(ClipList.SelectedIndex + step, 0, _items.Count - 1);
        ClipList.ScrollIntoView(ClipList.SelectedItem);
    }

    private void Paste(bool plain)
    {
        if (ClipList.SelectedItem is not ClipEntry clip) return;
        var target = _target;
        Hide();
        _service.PasteInto(clip, plain, target);
    }
}

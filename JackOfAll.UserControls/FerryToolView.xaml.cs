using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Services.Ferry;
using JackOfAll.UserControls.Services;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class FerryToolView : UserControl, IDisposable
{
    private readonly FerryService _service;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _url = "";
    private string _flash = "";
    private DateTime _flashUntil;
    private bool _disposed;

    internal FerryToolView(FerryService service)
    {
        InitializeComponent();
        _service = service;
        _service.Acquire();
        _service.Changed += OnChanged;
        _service.Status += Flash;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _tick.Tick += (_, _) => UpdateStatus();
        _tick.Start();

        AutoCopyCheck.IsChecked = _service.Settings.AutoCopy;
        LoadNetworks();
        RefreshFeed();
    }

    // ── link and QR code ───────────────────────────────────────

    private void LoadNetworks()
    {
        var networks = FerryNetworks.List();
        NetBox.ItemsSource = networks;
        NetBox.SelectedItem = networks.FirstOrDefault(n => n.Ip == _service.Settings.Ip) ?? networks.FirstOrDefault();
        NetBox.IsEnabled = networks.Count > 1;
        RefreshLink();
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(LoadNetworks);

    private void OnNetworkChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NetBox.SelectedItem is FerryAddress address && address.Ip != _service.Settings.Ip)
        {
            _service.Settings.Ip = address.Ip;
            _service.Settings.Save();
        }
        RefreshLink();
    }

    private void RefreshLink()
    {
        _url = _service.Url(NetBox.SelectedItem as FerryAddress);
        QrImage.Source = _url.Length > 0 ? QrEncoder.ToImage(_url) : null;
        QrMessage.Visibility = _url.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        QrMessage.Text = _service.Error ?? "Not connected to a network. Connect to Wi-Fi to get a code.";
        UrlText.Text = _url.Length > 0 ? _url[..(_url.IndexOf("/?", StringComparison.Ordinal) + 1)] : "";
        BtnCopyLink.IsEnabled = _url.Length > 0;
    }

    private void OnUrlClick(object sender, MouseButtonEventArgs e) => CopyLink();
    private void OnCopyLinkClick(object sender, RoutedEventArgs e) => CopyLink();

    private void CopyLink()
    {
        if (_url.Length == 0) return;
        if (TrySetClipboard(() => Clipboard.SetText(_url)))
            Flash("Link copied. Send it to your phone, or open it in another browser on this network.");
    }

    private void OnNewLinkClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Make a new link?\n\nPhones that have the current page open lose access until they scan the new QR code.",
            "Ferry", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _service.NewLink();
        RefreshLink();
    }

    // ── feed ───────────────────────────────────────────────────

    private void OnChanged()
    {
        RefreshFeed();
        RefreshLink();
    }

    private void RefreshFeed()
    {
        var selected = Feed.SelectedItem;
        var items = _service.Hub.Snapshot();
        Feed.ItemsSource = items;
        if (selected is not null && items.Contains(selected)) Feed.SelectedItem = selected;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var (when, device) = _service.Hub.LastSeen;
        var connected = (DateTime.Now - when).TotalSeconds < 30;
        string text;
        if (DateTime.Now < _flashUntil) text = _flash;
        else if (_service.Busy.Length > 0) text = _service.Busy;
        else if (_service.Error is not null) text = "Not running: " + _service.Error;
        else if (connected) text = "Connected to " + device;
        else if (when > DateTime.MinValue) text = $"{char.ToUpper(device[0])}{device[1..]} was last connected at {when:t}";
        else text = "Waiting for your phone";
        StatusText.Text = text;
        StatusDot.Fill = _service.Error is not null ? Brush("DangerBrush", Brushes.Red)
            : connected ? Brush("SuccessText", Brushes.Green) : Brush("TextDimmed", Brushes.Gray);
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void Flash(string message)
    {
        _flash = message;
        _flashUntil = DateTime.Now.AddSeconds(4);
        UpdateStatus();
    }

    private FerryItem? Selected => Feed.SelectedItem as FerryItem;

    private void OnFeedDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } item) Open(item);
    }

    private void OnFeedKeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { } item) return;
        if (e.Key == Key.Delete) { _service.Hub.Remove(item.Id); e.Handled = true; }
        else if (e.Key == Key.Enter) { Open(item); e.Handled = true; }
    }

    private void Open(FerryItem item)
    {
        if (item.IsText)
        {
            if (TrySetClipboard(() => Clipboard.SetText(item.Text))) Flash("Text copied to the clipboard.");
            return;
        }
        if (!File.Exists(item.Path)) { Flash("That file is no longer there."); return; }
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception exception) { Flash($"Couldn't open it: {exception.Message}"); }
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        foreach (var menuItem in ((ContextMenu)sender).Items.OfType<MenuItem>()) menuItem.IsEnabled = item is not null;
        if (item is null) return;
        MenuOpen.Header = item.IsText ? "Copy text" : "Open";
        MenuShowFolder.Visibility = item.IsText ? Visibility.Collapsed : Visibility.Visible;
        MenuCopy.Visibility = item.IsText ? Visibility.Collapsed : Visibility.Visible;
        MenuCopy.Header = "Copy file";
        MenuRemove.Header = item.ToPhone ? "Stop sharing" : "Remove from list";
    }

    private void OnMenuOpen(object sender, RoutedEventArgs e) { if (Selected is { } item) Open(item); }
    private void OnMenuRemove(object sender, RoutedEventArgs e) { if (Selected is { } item) _service.Hub.Remove(item.Id); }

    private void OnMenuShowFolder(object sender, RoutedEventArgs e)
    {
        if (Selected is { IsText: false } item) OpenFolder(item.Path);
    }

    private void OnMenuCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsText: false } item || !File.Exists(item.Path)) return;
        var files = new StringCollection { item.Path };
        if (TrySetClipboard(() => Clipboard.SetFileDropList(files))) Flash("File copied. Paste it in Explorer or any app.");
    }

    // ── sending to the phone ───────────────────────────────────

    private void OnComposerChanged(object sender, TextChangedEventArgs e) =>
        ComposerHint.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SendText(); e.Handled = true; }
    }

    private void OnSendTextClick(object sender, RoutedEventArgs e) => SendText();

    private void SendText()
    {
        if (string.IsNullOrWhiteSpace(Composer.Text)) { Composer.Focus(); return; }
        _service.SendText(Composer.Text);
        Composer.Clear();
        Composer.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.V) { SendClipboard(); e.Handled = true; }
        else if (e.Key == Key.O) { PickFiles(); e.Handled = true; }
    }

    private void OnSendFilesClick(object sender, RoutedEventArgs e) => PickFiles();

    private void PickFiles()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Send files to your phone" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _service.Share(dialog.FileNames);
    }

    private void OnSendClipboardClick(object sender, RoutedEventArgs e) => SendClipboard();

    private void SendClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
                _service.Share(Clipboard.GetFileDropList().Cast<string>());
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                var folder = Path.Combine(Path.GetTempPath(), "JackOfAll-Ferry");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"Clipboard {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(path)) encoder.Save(file);
                _service.Share([path]);
            }
            else if (Clipboard.ContainsText())
                _service.SendText(Clipboard.GetText());
            else
                Flash("The clipboard is empty.");
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException)
        {
            Flash("Another app is using the clipboard. Try again.");
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _service.Share(paths);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string { Length: > 0 } text) _service.SendText(text);
        e.Handled = true;
    }

    // ── received folder and options ────────────────────────────

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolder(null);

    private void OpenFolder(string? select)
    {
        try
        {
            if (select is not null && File.Exists(select)) Process.Start("explorer.exe", $"/select,\"{select}\"");
            else
            {
                Directory.CreateDirectory(_service.Settings.Folder);
                Process.Start("explorer.exe", $"\"{_service.Settings.Folder}\"");
            }
        }
        catch (Exception exception) { Flash($"Couldn't open the folder: {exception.Message}"); }
    }

    private void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Where should files from your phone go?", InitialDirectory = _service.Settings.Folder };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _service.Settings.Folder = dialog.FolderName;
        _service.Settings.Save();
        Flash($"Files from your phone now go to {dialog.FolderName}");
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => _service.Hub.Clear();

    private void OnAutoCopyChanged(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        _service.Settings.AutoCopy = AutoCopyCheck.IsChecked == true;
        _service.Settings.Save();
    }

    private bool TrySetClipboard(Action set)
    {
        try
        {
            set();
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Flash("Another app is using the clipboard. Try again.");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _service.Changed -= OnChanged;
        _service.Status -= Flash;
        _service.Release();
    }
}

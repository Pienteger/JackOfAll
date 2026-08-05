using System.Collections.ObjectModel;
using System.Text;
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
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        _filtered.Clear();
        foreach (var clip in _service.Clips)
        {
            if (string.IsNullOrEmpty(query) ||
                clip.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                _filtered.Add(clip);
        }

        var total = _service.Clips.Count;
        var pinned = _service.Clips.Count(static clip => clip.IsPinned);
        CountText.Text = pinned > 0 ? $"{total} clips | {pinned} pinned" : $"{total} clips";

        PauseIcon.Text = _service.IsPaused ? "\uE768" : "\uE769";
        PauseLabel.Text = _service.IsPaused ? "Resume" : "Pause";
        MonitorDot.Fill = _service.IsPaused
            ? (TryFindResource("WarningBrush") as Brush ?? Brushes.Orange)
            : (TryFindResource("SuccessText") as Brush ?? Brushes.Green);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipEntry clip })
            _service.Copy(clip.Content);
    }

    private void OnClipDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClipList.SelectedItem is ClipEntry clip)
            _service.Copy(clip.Content);
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

public record ClipEntry(string Content, DateTime CopiedAt, bool IsPinned)
{
    private static readonly Brush AccentBar = CreateFrozenBrush(Color.FromRgb(0x63, 0x66, 0xf1));
    private static readonly Brush TransparentBar = Brushes.Transparent;

    public string Preview
    {
        get
        {
            var text = Content.Length > 300 ? Content[..300] : Content;
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            return lines.Length > 2
                ? string.Join(" ", lines[..Math.Min(3, lines.Length)]).TrimEnd() + "..."
                : text.ReplaceLineEndings(" ");
        }
    }

    public string TimestampText => CopiedAt.Date == DateTime.Today
        ? CopiedAt.ToString("HH:mm:ss")
        : CopiedAt.ToString("MMM dd, HH:mm");

    public string SizeText
    {
        get
        {
            var bytes = Encoding.UTF8.GetByteCount(Content);
            return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:F1} KB";
        }
    }

    public string LineCountText
    {
        get
        {
            var lines = Content.Count(static character => character == '\n') + 1;
            return lines > 1 ? $"  |  {lines} lines" : "";
        }
    }

    public string PinGlyph => IsPinned ? "\uE840" : "\uE718";
    public string PinTooltip => IsPinned ? "Unpin" : "Pin";
    public Brush PinForeground => IsPinned ? AccentBar : TransparentBar;
    public Brush PinBarBrush => IsPinned ? AccentBar : TransparentBar;

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
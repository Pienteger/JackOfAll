using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services.Tally;

namespace JackOfAll.UserControls;

/// <summary>Tally: automatic time tracking. The recorder runs from app start; this view only reads what it stored.</summary>
public partial class TallyToolView : UserControl, IStatefulTool
{
    private static readonly string[] Groupings = ["App", "Window title", "Site", "Project"];
    private const double BarMax = 160;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4F, 0x8E, 0xF7), Color.FromRgb(0x3C, 0xB3, 0x71), Color.FromRgb(0xE8, 0x9A, 0x3C),
        Color.FromRgb(0xC2, 0x5B, 0xD6), Color.FromRgb(0xE0, 0x5A, 0x6B), Color.FromRgb(0x2B, 0xB5, 0xC0),
        Color.FromRgb(0xA3, 0x8B, 0x5C), Color.FromRgb(0x8A, 0x9A, 0x2E), Color.FromRgb(0x6C, 0x6F, 0xD8),
        Color.FromRgb(0xD8, 0x6F, 0x9F)
    ];

    private readonly TallyRecorder _recorder;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly ObservableCollection<TallyRule> _rules;
    private DateTime _day = DateTime.Today;
    private List<TallySpan> _spans = [];
    private Dictionary<string, Brush> _brushes = new();

    public sealed record Row(string Name, double Seconds, string TimeText, string ShareText, double BarWidth, Brush Brush);

    public TallyToolView(TallyRecorder recorder)
    {
        InitializeComponent();
        _recorder = recorder;
        GroupBox.ItemsSource = Groupings;
        GroupBox.SelectedIndex = 0;
        FieldColumn.ItemsSource = TallyRules.Fields;
        _rules = new ObservableCollection<TallyRule>(TallyRules.Load());
        RulesGrid.ItemsSource = _rules;

        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _recorder.Changed += OnRecorderChanged; _refresh.Start(); Refresh(); };
        Unloaded += (_, _) => { _recorder.Changed -= OnRecorderChanged; _refresh.Stop(); };
        PreviewKeyDown += OnKey;
    }

    public Dictionary<string, string> SaveState() => new()
    {
        ["group"] = GroupBox.SelectedIndex.ToString(),
        ["tab"] = Tabs.SelectedIndex.ToString()
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("group", out var g) && int.TryParse(g, out var gi) && gi >= 0 && gi < Groupings.Length)
            GroupBox.SelectedIndex = gi;
        if (state.TryGetValue("tab", out var t) && int.TryParse(t, out var ti) && ti >= 0 && ti < Tabs.Items.Count)
            Tabs.SelectedIndex = ti;
    }

    private bool IsWeek => Tabs.SelectedIndex == 1;

    private void OnRecorderChanged() => UpdateStatus();

    // ── navigation ─────────────────────────────────────────────

    private void OnPrevClick(object sender, RoutedEventArgs e) => Move(-1);
    private void OnNextClick(object sender, RoutedEventArgs e) => Move(1);
    private void OnTodayClick(object sender, RoutedEventArgs e) { _day = DateTime.Today; Refresh(); }

    private void Move(int dir)
    {
        _day = _day.AddDays(IsWeek ? 7 * dir : dir);
        if (_day > DateTime.Today) _day = DateTime.Today;
        Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Left) { Move(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { Move(1); e.Handled = true; }
    }

    private void OnGroupChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Refresh(); }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs && IsLoaded) Refresh();
    }

    private void OnTimelineSized(object sender, SizeChangedEventArgs e) => DrawTimeline();

    // ── data ───────────────────────────────────────────────────

    private string KeyOf(TallySpan s, IReadOnlyList<TallyRule> rules) => GroupBox.SelectedIndex switch
    {
        1 => s.Title.Length == 0 ? $"({s.App})" : s.Title,
        2 => TallyRecorder.SiteOf(s),
        3 => TallyRules.ProjectOf(s, rules),
        _ => s.App
    };

    private void Refresh()
    {
        var rules = _rules.ToList();
        NameColumn.Header = Groupings[Math.Max(0, GroupBox.SelectedIndex)];
        BtnForget.Visibility = IsWeek ? Visibility.Collapsed : Visibility.Visible;
        if (IsWeek) RefreshWeek(rules);
        else RefreshDay(rules);
        UpdateStatus();
    }

    private void RefreshDay(List<TallyRule> rules)
    {
        DateLabel.Text = _day == DateTime.Today ? $"Today, {_day:d MMM}" : _day.ToString("ddd, d MMM yyyy");
        _spans = _recorder.Query(_day, _day.AddDays(1));

        var totals = _spans.GroupBy(s => KeyOf(s, rules))
            .Select(g => (Name: g.Key, Seconds: g.Sum(s => s.Duration.TotalSeconds)))
            .OrderByDescending(x => x.Seconds)
            .ToList();
        var all = totals.Sum(t => t.Seconds);
        var max = totals.Count > 0 ? totals[0].Seconds : 1;

        _brushes = new Dictionary<string, Brush>();
        for (var i = 0; i < totals.Count; i++)
            _brushes[totals[i].Name] = BrushFor(i);

        TotalsGrid.ItemsSource = totals.Select(t => new Row(
            t.Name, t.Seconds, Format(t.Seconds),
            all > 0 ? $"{t.Seconds / all:P0}" : "",
            BarMax * t.Seconds / max, _brushes[t.Name])).ToList();
        TotalText.Text = all > 0 ? $"{Format(all)} active" : "";
        DrawTimeline();
    }

    private void RefreshWeek(List<TallyRule> rules)
    {
        var monday = _day.AddDays(-(((int)_day.DayOfWeek + 6) % 7));
        DateLabel.Text = $"{monday:d MMM} – {monday.AddDays(6):d MMM yyyy}";
        var spans = _recorder.Query(monday, monday.AddDays(7));

        var table = new DataTable();
        table.Columns.Add(Groupings[Math.Max(0, GroupBox.SelectedIndex)]);
        for (var d = 0; d < 7; d++) table.Columns.Add(monday.AddDays(d).ToString("ddd d"));
        table.Columns.Add("Total");

        var byKey = spans.GroupBy(s => KeyOf(s, rules))
            .Select(g => (Key: g.Key, Days: Enumerable.Range(0, 7)
                .Select(d => g.Where(s => s.Start.Date == monday.AddDays(d)).Sum(s => s.Duration.TotalSeconds)).ToArray()))
            .OrderByDescending(x => x.Days.Sum())
            .ToList();

        foreach (var (key, days) in byKey)
        {
            var row = table.NewRow();
            row[0] = key;
            for (var d = 0; d < 7; d++) row[d + 1] = days[d] >= 60 ? Format(days[d]) : "";
            row[8] = Format(days.Sum());
            table.Rows.Add(row);
        }
        if (byKey.Count > 0)
        {
            var sum = table.NewRow();
            sum[0] = "Total";
            for (var d = 0; d < 7; d++)
            {
                var s = byKey.Sum(x => x.Days[d]);
                sum[d + 1] = s >= 60 ? Format(s) : "";
            }
            sum[8] = Format(byKey.Sum(x => x.Days.Sum()));
            table.Rows.Add(sum);
        }
        WeekGrid.ItemsSource = table.DefaultView;
        TotalText.Text = byKey.Count > 0 ? $"{Format(byKey.Sum(x => x.Days.Sum()))} this week" : "";
    }

    private void OnWeekColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var first = e.PropertyName == Groupings[Math.Max(0, GroupBox.SelectedIndex)];
        e.Column.Width = first ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(78);
    }

    private void DrawTimeline()
    {
        Timeline.Children.Clear();
        HourAxis.Children.Clear();
        var w = Timeline.ActualWidth;
        if (w <= 0) return;

        var track = new Rectangle { Width = w, Height = Timeline.Height, RadiusX = 3, RadiusY = 3 };
        track.SetResourceReference(Shape.FillProperty, "AppBackground");
        Timeline.Children.Add(track);

        foreach (var s in _spans)
        {
            var x0 = (s.Start - _day).TotalHours / 24 * w;
            var x1 = (s.End - _day).TotalHours / 24 * w;
            var key = KeyOf(s, _rules.ToList());
            var rect = new Rectangle
            {
                Width = Math.Max(1, x1 - x0),
                Height = Timeline.Height,
                Fill = _brushes.TryGetValue(key, out var b) ? b : Brushes.Gray,
                ToolTip = $"{s.Start:HH:mm}–{s.End:HH:mm}  ({Format(s.Duration.TotalSeconds)})\n{s.App}\n{s.Title}"
            };
            Canvas.SetLeft(rect, x0);
            Timeline.Children.Add(rect);
        }

        if (_day == DateTime.Today)
        {
            var now = new Rectangle { Width = 2, Height = Timeline.Height };
            now.SetResourceReference(Shape.FillProperty, "TextPrimary");
            Canvas.SetLeft(now, (DateTime.Now - _day).TotalHours / 24 * w);
            Timeline.Children.Add(now);
        }

        for (var h = 0; h <= 24; h += 3)
        {
            var label = new TextBlock { Text = h == 24 ? "" : $"{h:00}:00", FontSize = 10 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimmed");
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.Margin = new Thickness(h / 24.0 * w, 0, 0, 0);
            HourAxis.Children.Add(label);
        }
    }

    private static Brush BrushFor(int i)
    {
        var c = Palette[i % Palette.Length];
        var b = new SolidColorBrush(i < Palette.Length ? c : Color.FromArgb(150, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m";
        return $"{t.Seconds}s";
    }

    private void UpdateStatus()
    {
        var state = _recorder.IsIdle
            ? "Paused while you are away"
            : $"Recording · stops counting after {TallyRecorder.IdleAfter.TotalMinutes:0} min idle";
        StatusText.Text = _recorder.LastError is { } err
            ? $"Could not save: {err}"
            : $"{state} · kept only on this PC in {TallyRecorder.DbPath}";
    }

    // ── actions ────────────────────────────────────────────────

    private void OnForgetClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ConfirmationDialog("Tally", "Forget this day?",
            $"Delete everything Tally recorded on {_day:dddd d MMMM}. This cannot be undone.", "Forget")
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _recorder.Forget(_day, _day.AddDays(1));
        Refresh();
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        var rule = new TallyRule();
        _rules.Add(rule);
        RulesGrid.SelectedItem = rule;
        RulesGrid.ScrollIntoView(rule);
    }

    private void OnSaveRulesClick(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        TallyRules.Save(_rules);
        var kept = TallyRules.Load();
        _rules.Clear();
        foreach (var r in kept) _rules.Add(r);
        RulesStatus.Text = $"Saved {kept.Count} rule{(kept.Count == 1 ? "" : "s")}.";
        Refresh();
    }
}

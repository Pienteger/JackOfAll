using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services.Hush;

namespace JackOfAll.UserControls;

/// <summary>Hush: an ambient sound mixer. Every sound is synthesized live; there are no recordings.</summary>
public partial class HushToolView : UserControl, IStatefulTool, IDisposable
{
    private static readonly int[] TimerMinutes = [0, 15, 30, 60, 90, 120];

    private readonly Engine _engine;
    private readonly List<Sound> _sounds;
    private readonly List<Tile> _tiles = [];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private string? _activeMix;
    private int _timerIdx;
    private DateTime _sleepEnd;
    private bool _syncing, _disposed;

    private sealed record Tile(Border Card, Border Tint, TextBlock Glyph, TextBlock Name, TextBlock Pct, Slider Slider);

    public HushToolView()
    {
        InitializeComponent();
        _engine = HushPlayer.Acquire();
        _sounds = HushPlayer.Sounds;

        for (var i = 0; i < _sounds.Count; i++) TileGrid.Children.Add(BuildTile(i));
        BuildMixes();

        _syncing = true;
        MasterSlider.Value = _engine.Master;
        _syncing = false;

        _tick.Tick += (_, _) => OnTick();
        Loaded += (_, _) => { if (!_disposed) _tick.Start(); };
        Unloaded += (_, _) => _tick.Stop(); // switching tabs; the sound keeps playing
        PreviewKeyDown += OnKey;
        RefreshAll();
    }

    // ── tiles ──────────────────────────────────────────────────────

    private UIElement BuildTile(int i)
    {
        var s = _sounds[i];
        var color = ColorOf(s);

        var glyph = new TextBlock { Text = s.Glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 24 };
        var key = new TextBlock { Text = ((i + 1) % 10).ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        key.SetResourceReference(TextBlock.ForegroundProperty, "TextDimmed");
        var name = new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var pct = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = s.Vol, Margin = new Thickness(0, 6, 0, 0), SmallChange = 0.05, LargeChange = 0.1 };
        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            s.Vol = (float)e.NewValue;
            if (!s.On && s.Vol > 0) SetOn(i, true);
            _activeMix = null;
            RefreshTile(i);
            RefreshMixes();
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(glyph);
        grid.Children.Add(key);
        var nameRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pct, Dock.Right);
        nameRow.Children.Add(pct);
        nameRow.Children.Add(name);
        Grid.SetRow(nameRow, 2);
        Grid.SetRow(slider, 3);
        grid.Children.Add(nameRow);
        grid.Children.Add(slider);

        var tint = new Border { CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B)) };
        var inner = new Grid();
        inner.Children.Add(tint);
        inner.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 10), Child = grid });

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 10, 10),
            MinHeight = 118,
            Cursor = Cursors.Hand,
            Child = inner,
            ToolTip = $"{s.Name}: click to turn on or off, drag the slider or scroll for volume (key {(i + 1) % 10})"
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (IsInside(slider, e.OriginalSource as DependencyObject)) return;
            SetOn(i, !s.On);
        };
        card.MouseWheel += (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
            e.Handled = true;
        };

        _tiles.Add(new Tile(card, tint, glyph, name, pct, slider));
        return card;
    }

    private static bool IsInside(DependencyObject container, DependencyObject? element)
    {
        for (var x = element; x is not null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x == container) return true;
        return false;
    }

    private static Color ColorOf(Sound s) => Color.FromRgb(s.Color.R, s.Color.G, s.Color.B);

    private void RefreshTile(int i)
    {
        var s = _sounds[i];
        var t = _tiles[i];
        t.Tint.Visibility = s.On ? Visibility.Visible : Visibility.Collapsed;
        if (s.On)
        {
            var c = new SolidColorBrush(ColorOf(s));
            t.Card.BorderBrush = c;
            t.Glyph.Foreground = c;
            t.Pct.Foreground = c;
            t.Name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        }
        else
        {
            t.Card.SetResourceReference(Border.BorderBrushProperty, "SurfaceBorder");
            t.Glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextDimmed");
            t.Pct.SetResourceReference(TextBlock.ForegroundProperty, "TextDimmed");
            t.Name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        }
        t.Pct.Text = s.On ? $"{Math.Round(s.Vol * 100)}%" : "off";
        _syncing = true;
        t.Slider.Value = s.Vol;
        _syncing = false;
    }

    private void SetOn(int i, bool on)
    {
        var s = _sounds[i];
        s.On = on;
        if (on && s.Vol < 0.05f) s.Vol = 0.5f;
        if (on && !_engine.Playing) Play(true);
        _activeMix = null;
        RefreshTile(i);
        RefreshMixes();
        RefreshStatus();
    }

    // ── mixes ──────────────────────────────────────────────────────

    private void BuildMixes()
    {
        for (var p = 0; p < Presets.Names.Length; p++)
        {
            var index = p;
            var b = new Button { Content = Presets.Names[p], Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 8), Tag = Presets.Names[p] };
            b.Click += (_, _) => ApplyMix(index);
            MixPanel.Children.Add(b);
        }
    }

    private void ApplyMix(int p)
    {
        Presets.Apply(p, _sounds);
        _activeMix = Presets.Names[p];
        Play(true);
        RefreshAll();
    }

    private void RefreshMixes()
    {
        var primary = (Style)FindResource("PrimaryButton");
        foreach (Button b in MixPanel.Children)
        {
            if ((string)b.Tag == _activeMix)
            {
                b.Style = primary;
                b.ClearValue(BackgroundProperty);
                b.ClearValue(ForegroundProperty);
                b.ClearValue(BorderBrushProperty);
            }
            else
            {
                b.ClearValue(StyleProperty);
                b.SetResourceReference(BackgroundProperty, "SurfaceBackground");
                b.SetResourceReference(ForegroundProperty, "TextPrimary");
                b.SetResourceReference(BorderBrushProperty, "SurfaceBorder");
            }
        }
    }

    // ── transport, volume, sleep timer ─────────────────────────────

    private void OnPlayClick(object sender, RoutedEventArgs e) => Play(!_engine.Playing);

    private void Play(bool play)
    {
        _engine.Playing = play;
        if (play && _timerIdx == 0) _engine.Fade = 1f;
        PlayGlyph.Text = play ? "" : "";
        PlayLabel.Text = play ? "Pause" : "Play";
        RefreshStatus();
    }

    private void OnMasterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MasterText.Text = $"{Math.Round(e.NewValue * 100)}%";
        if (!_syncing) _engine.Master = (float)e.NewValue;
    }

    private void OnTimerClick(object sender, RoutedEventArgs e) => CycleTimer(1);

    private void OnTimerRightClick(object sender, MouseButtonEventArgs e)
    {
        CycleTimer(-1);
        e.Handled = true;
    }

    private void CycleTimer(int dir)
    {
        _timerIdx = (_timerIdx + dir + TimerMinutes.Length) % TimerMinutes.Length;
        if (_timerIdx > 0)
        {
            _sleepEnd = DateTime.Now.AddMinutes(TimerMinutes[_timerIdx]);
            if (!_engine.Playing) Play(true);
        }
        _engine.Fade = 1f;
        RefreshTimer();
    }

    private void RefreshTimer()
    {
        if (_timerIdx == 0)
        {
            TimerLabel.Text = "Sleep timer: off";
            return;
        }
        var rem = _sleepEnd - DateTime.Now;
        if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
        TimerLabel.Text = $"Fades out in {(int)rem.TotalMinutes}:{rem.Seconds:00}";
    }

    private void OnTick()
    {
        Viz.Step(_sounds);
        if (_timerIdx > 0)
        {
            var rem = (_sleepEnd - DateTime.Now).TotalSeconds;
            _engine.Fade = (float)Math.Clamp(rem / 60.0, 0, 1); // fade over the last minute
            if (rem <= 0)
            {
                _timerIdx = 0;
                Play(false);
            }
            RefreshTimer();
        }
        VizHint.Text = _engine.Error ?? (Viz.HasMotion ? "" : _engine.Playing ? "Silence. Turn on a sound or pick a mix." : "Paused. Press Play or pick a mix.");
    }

    private void RefreshStatus()
    {
        var on = _sounds.Where(s => s.On).Select(s => s.Name).ToList();
        StatusText.Text = _engine.Error ?? (on.Count == 0 ? "Nothing selected" : (_engine.Playing ? "Playing  " : "Paused  ") + string.Join(", ", on));
    }

    private void RefreshAll()
    {
        for (var i = 0; i < _tiles.Count; i++) RefreshTile(i);
        RefreshMixes();
        RefreshStatus();
        RefreshTimer();
        Play(_engine.Playing);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Space)
        {
            Play(!_engine.Playing);
            e.Handled = true;
        }
        else if (e.Key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            var i = e.Key == Key.D0 ? 9 : e.Key - Key.D1;
            if (i < _sounds.Count) SetOn(i, !_sounds[i].On);
            e.Handled = true;
        }
        else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.None)
        {
            CycleTimer(1);
            e.Handled = true;
        }
    }

    // ── IStatefulTool / IDisposable ────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["master"] = _engine.Master.ToString(CultureInfo.InvariantCulture),
            ["mix"] = _activeMix ?? ""
        };
        foreach (var s in _sounds)
            state["sound:" + s.Name] = (s.On ? "1" : "0") + "|" + s.Vol.ToString(CultureInfo.InvariantCulture);
        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("master", out var m) && float.TryParse(m, NumberStyles.Float, CultureInfo.InvariantCulture, out var master))
        {
            _engine.Master = Math.Clamp(master, 0, 1);
            _syncing = true;
            MasterSlider.Value = _engine.Master;
            _syncing = false;
            MasterText.Text = $"{Math.Round(_engine.Master * 100)}%";
        }
        foreach (var s in _sounds)
        {
            if (!state.TryGetValue("sound:" + s.Name, out var v)) continue;
            var parts = v.Split('|');
            if (parts.Length != 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vol)) continue;
            s.On = parts[0] == "1";
            s.Vol = Math.Clamp(vol, 0, 1);
        }
        _activeMix = state.TryGetValue("mix", out var mix) && mix.Length > 0 ? mix : null;
        RefreshAll(); // restored tabs stay paused until you press Play
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        HushPlayer.Release();
    }
}

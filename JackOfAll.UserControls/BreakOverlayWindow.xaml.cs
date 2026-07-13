using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace JackOfAll.UserControls;

public partial class BreakOverlayWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan _remaining;
    private TimeSpan _total;
    private int _skipAttempts;

    public BreakOverlayWindow(int breakSeconds)
    {
        InitializeComponent();

        _remaining = TimeSpan.FromSeconds(breakSeconds);
        _total = _remaining;
        UpdateDisplay();

        _timer.Tick += OnTick;
        _timer.Start();

        KeyDown += OnKeyDown;
        MouseDown += (_, _) => BringToFront();
        Deactivated += (_, _) => BringToFront();
        Loaded += (_, _) => CoverAllScreens();
    }

    private void CoverAllScreens()
    {
        WindowState = WindowState.Normal;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Topmost = true;
        Activate();
        Focus();
    }

    private void BringToFront()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Topmost = true;
            Activate();
            Focus();
        }, DispatcherPriority.Input);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            Close();
            return;
        }

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        CountdownText.Text = _remaining.ToString(@"m\:ss");
        var elapsed = _total - _remaining;
        BreakProgress.Value = elapsed.TotalSeconds / _total.TotalSeconds * 100;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _skipAttempts++;
            if (_skipAttempts < 3)
            {
                SkipHint.Text = $"Press Escape {3 - _skipAttempts} more time(s) to skip the break.";
            }
            else
            {
                _timer.Stop();
                Close();
            }
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_remaining > TimeSpan.Zero && _skipAttempts < 3)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}

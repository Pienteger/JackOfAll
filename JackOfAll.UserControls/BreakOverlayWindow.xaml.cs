using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace JackOfAll.UserControls;

public partial class BreakOverlayWindow : Window
{
    private readonly Rect? _screenBounds;
    private readonly Func<SkipAttemptResult>? _recordSkipAttempt;
    private readonly Action<string>? _broadcastSkipHint;
    private readonly Action? _requestCloseAll;
    private TimeSpan _remaining;
    private TimeSpan _total;
    private int _skipAttempts;
    private bool _allowClose;

    public BreakOverlayWindow(int breakSeconds)
        : this(breakSeconds, null, null, null, null)
    {
    }

    private BreakOverlayWindow(
        int breakSeconds,
        Rect? screenBounds,
        Func<SkipAttemptResult>? recordSkipAttempt,
        Action<string>? broadcastSkipHint,
        Action? requestCloseAll)
    {
        InitializeComponent();

        _screenBounds = screenBounds;
        _recordSkipAttempt = recordSkipAttempt;
        _broadcastSkipHint = broadcastSkipHint;
        _requestCloseAll = requestCloseAll;
        _remaining = TimeSpan.FromSeconds(breakSeconds);
        _total = _remaining;
        UpdateDisplay(_remaining, _total);

        KeyDown += OnKeyDown;
        MouseDown += (_, _) => BringToFront();
        Loaded += (_, _) => CoverScreen();
    }

    public static void ShowForAllScreens(int breakSeconds)
    {
        var screens = GetMonitorBounds();
        if (screens.Count == 0)
            screens.Add(new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));

        var windows = new List<BreakOverlayWindow>();
        var frame = new DispatcherFrame();
        var closingAll = false;
        var sharedSkipAttempts = 0;
        var total = TimeSpan.FromSeconds(Math.Max(1, breakSeconds));
        var remaining = total;
        var endsAt = DateTime.UtcNow + total;

        SkipAttemptResult RecordSkipAttempt()
        {
            sharedSkipAttempts++;
            return new SkipAttemptResult(sharedSkipAttempts, Math.Max(0, 3 - sharedSkipAttempts), sharedSkipAttempts >= 3);
        }

        void BroadcastSkipHint(string message)
        {
            foreach (var window in windows)
                window.SkipHint.Text = message;
        }

        void CloseAll()
        {
            if (closingAll)
                return;

            closingAll = true;
            frame.Continue = false;

            foreach (var window in windows.ToList())
            {
                window.AllowClose();
                if (window.IsVisible)
                    window.Close();
            }
        }

        foreach (var screen in screens)
            windows.Add(new BreakOverlayWindow(breakSeconds, screen, RecordSkipAttempt, BroadcastSkipHint, CloseAll));

        foreach (var window in windows)
            window.Closed += (_, _) => CloseAll();

        foreach (var window in windows)
            window.Show();

        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        timer.Tick += (_, _) =>
        {
            remaining = endsAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                timer.Stop();
                CloseAll();
                return;
            }

            foreach (var window in windows)
                window.UpdateDisplay(remaining, total);
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        CloseAll();
    }

    private void CoverScreen()
    {
        WindowState = WindowState.Normal;

        if (_screenBounds is { } bounds)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        else
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        Topmost = true;
        Focus();
    }

    private void BringToFront()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Topmost = false;
            Topmost = true;
            Focus();
        }, DispatcherPriority.Background);
    }

    private void UpdateDisplay(TimeSpan remaining, TimeSpan total)
    {
        _remaining = remaining;
        _total = total;
        CountdownText.Text = remaining.ToString(@"m\:ss");

        if (total.TotalSeconds <= 0)
        {
            BreakProgress.Value = 100;
            return;
        }

        var elapsed = total - remaining;
        BreakProgress.Value = Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var attempt = _recordSkipAttempt?.Invoke() ?? RecordLocalSkipAttempt();
            _skipAttempts = attempt.Attempts;

            if (!attempt.ShouldClose)
            {
                var message = $"Press Escape {attempt.Remaining} more time(s) to skip the break.";
                SkipHint.Text = message;
                _broadcastSkipHint?.Invoke(message);
            }
            else
            {
                AllowClose();
                _requestCloseAll?.Invoke();
                Close();
            }
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    private SkipAttemptResult RecordLocalSkipAttempt()
    {
        _skipAttempts++;
        return new SkipAttemptResult(_skipAttempts, Math.Max(0, 3 - _skipAttempts), _skipAttempts >= 3);
    }

    private void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && _remaining > TimeSpan.Zero && _skipAttempts < 3)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private static List<Rect> GetMonitorBounds()
    {
        var bounds = new List<Rect>();
        EnumDisplayMonitors(nint.Zero, nint.Zero, (_, _, rect, _) =>
        {
            bounds.Add(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, nint.Zero);
        return bounds;
    }

    private sealed record SkipAttemptResult(int Attempts, int Remaining, bool ShouldClose);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, MonitorRect rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MonitorRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

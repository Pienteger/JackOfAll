using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class WellbeingToolView : UserControl
{
    private readonly DispatcherTimer _pomodoroTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _reminderTimer = new();
    private TimeSpan _remaining;
    private TimeSpan _totalDuration;
    private bool _isWorking = true;
    private bool _isPomodoroRunning;
    private int _sessions;

    public WellbeingToolView()
    {
        InitializeComponent();
        _pomodoroTimer.Tick += OnPomodoroTick;
        _reminderTimer.Tick += OnReminderTick;
        ResetPomodoro();

        Unloaded += (_, _) =>
        {
            _pomodoroTimer.Stop();
            _reminderTimer.Stop();
        };
    }

    // -- Pomodoro --------------------------------------------------------

    private void OnStartPauseClick(object sender, RoutedEventArgs e)
    {
        if (_isPomodoroRunning)
        {
            _pomodoroTimer.Stop();
            _isPomodoroRunning = false;
            BtnStartPause.Content = "Resume";
            TimerLabel.Text = "Paused";
        }
        else
        {
            _pomodoroTimer.Start();
            _isPomodoroRunning = true;
            BtnStartPause.Content = "Pause";
            TimerLabel.Text = _isWorking ? "Focus time" : "Break time";
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetPomodoro();

    private void OnSkipClick(object sender, RoutedEventArgs e) => SwitchPhase();

    private void OnSettingsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isPomodoroRunning)
            ResetPomodoro();
    }

    private void OnPomodoroTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            WellbeingService.PlayNotificationSound();
            if (_isWorking)
                _sessions++;
            SessionCount.Text = _sessions.ToString();
            SwitchPhase();
            return;
        }

        UpdateTimerDisplay();
    }

    private void SwitchPhase()
    {
        _isWorking = !_isWorking;

        if (_isWorking)
        {
            var mins = GetMinutes(WorkMinutes, 25);
            _remaining = TimeSpan.FromMinutes(mins);
            _totalDuration = _remaining;
            TimerLabel.Text = _isPomodoroRunning ? "Focus time" : "Ready to focus";
        }
        else
        {
            var mins = GetMinutes(BreakMinutes, 5);
            _remaining = TimeSpan.FromMinutes(mins);
            _totalDuration = _remaining;
            TimerLabel.Text = "Break time";
            WellbeingService.PlayNotificationSound();
        }

        UpdateTimerDisplay();
    }

    private void ResetPomodoro()
    {
        _pomodoroTimer.Stop();
        _isPomodoroRunning = false;
        _isWorking = true;
        var mins = GetMinutes(WorkMinutes, 25);
        _remaining = TimeSpan.FromMinutes(mins);
        _totalDuration = _remaining;
        BtnStartPause.Content = "Start";
        TimerLabel.Text = "Ready to focus";
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        TimerDisplay.Text = _remaining.ToString(@"mm\:ss");

        if (_totalDuration.TotalSeconds > 0)
        {
            var elapsed = _totalDuration - _remaining;
            TimerProgress.Value = elapsed.TotalSeconds / _totalDuration.TotalSeconds * 100;
        }

        TimerDisplay.Foreground = _isWorking
            ? FindBrush("HeaderColor")
            : FindBrush("PayloadColor");
    }

    private static int GetMinutes(TextBox box, int fallback)
    {
        if (int.TryParse(box.Text, out var val) && val > 0 && val <= 120)
            return val;
        return fallback;
    }

    // -- Break Reminder --------------------------------------------------

    private void OnToggleReminderClick(object sender, RoutedEventArgs e)
    {
        if (_reminderTimer.IsEnabled)
        {
            _reminderTimer.Stop();
            BtnToggleReminder.Content = "Enable";
            ReminderStatus.Text = "Disabled";
        }
        else
        {
            if (!int.TryParse(ReminderInterval.Text, out var mins) || mins < 1)
                mins = 20;
            _reminderTimer.Interval = TimeSpan.FromMinutes(mins);
            _reminderTimer.Start();
            BtnToggleReminder.Content = "Disable";
            ReminderStatus.Text = $"Active. Next reminder in {mins} minutes.";
        }
    }

    private void OnReminderTick(object? sender, EventArgs e)
    {
        WellbeingService.PlayNotificationSound();

        var mins = _reminderTimer.Interval.TotalMinutes;
        ReminderStatus.Text = $"Break active! Next in {mins:0} min.";

        var breakSecs = 20;
        if (int.TryParse(BreakDuration.Text, out var val) && val >= 5 && val <= 300)
            breakSecs = val;

        var overlay = new BreakOverlayWindow(breakSecs);
        overlay.ShowDialog();

        ReminderStatus.Text = $"Active. Next reminder in {mins:0} minutes.";
    }

    // -- Helpers ----------------------------------------------------------

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.White;
}

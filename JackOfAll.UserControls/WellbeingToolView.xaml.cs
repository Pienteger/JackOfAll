using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class WellbeingToolView : UserControl, IStatefulTool
{
    private readonly DispatcherTimer _pomodoroTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _reminderTimer = new();
    private readonly DispatcherTimer _breathTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string[] _breathSteps = ["Inhale", "Hold", "Exhale", "Hold"];

    private TimeSpan _remaining;
    private TimeSpan _totalDuration;
    private TimeSpan _focusToday;
    private bool _isWorking = true;
    private bool _isLongBreak;
    private bool _isPomodoroRunning;
    private int _sessions;

    private TimeSpan _breathRemaining = TimeSpan.FromMinutes(1);
    private int _breathStepIndex;
    private int _breathStepRemaining = 4;
    private bool _isBreathingRunning;

    public WellbeingToolView()
    {
        InitializeComponent();
        _pomodoroTimer.Tick += OnPomodoroTick;
        _reminderTimer.Tick += OnReminderTick;
        _breathTimer.Tick += OnBreathTick;
        ResetPomodoro();
        ResetBreathing();

        Unloaded += (_, _) =>
        {
            _pomodoroTimer.Stop();
            _reminderTimer.Stop();
            _breathTimer.Stop();
        };
    }

    private void OnStartPauseClick(object sender, RoutedEventArgs e)
    {
        if (_isPomodoroRunning)
            PausePomodoro();
        else
            StartPomodoro();
    }

    private void StartPomodoro()
    {
        _pomodoroTimer.Start();
        _isPomodoroRunning = true;
        SetStartPauseButton(false);
        TimerLabel.Text = _isWorking ? "Focus time" : _isLongBreak ? "Long break" : "Break time";
        UpdatePhaseDisplay();
    }

    private void PausePomodoro()
    {
        _pomodoroTimer.Stop();
        _isPomodoroRunning = false;
        SetStartPauseButton(true, "Resume");
        TimerLabel.Text = "Paused";
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetPomodoro();

    private void OnSkipClick(object sender, RoutedEventArgs e) => SwitchPhase(false);

    private void OnSettingsChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !_isPomodoroRunning)
            ResetPomodoro();
    }

    private void OnPresetClassic(object sender, RoutedEventArgs e) => ApplyPreset(25, 5, 15);

    private void OnPresetDeepWork(object sender, RoutedEventArgs e) => ApplyPreset(50, 10, 20);

    private void OnPresetUltradian(object sender, RoutedEventArgs e) => ApplyPreset(90, 15, 30);

    private void ApplyPreset(int work, int brk, int longBreak)
    {
        WorkMinutes.Text = work.ToString();
        BreakMinutes.Text = brk.ToString();
        LongBreakMinutes.Text = longBreak.ToString();
        if (!_isPomodoroRunning)
            ResetPomodoro();
    }

    private void OnPomodoroTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            WellbeingService.PlayNotificationSound();
            SwitchPhase(true);
            return;
        }

        UpdateTimerDisplay();
    }

    private void SwitchPhase(bool completedCurrentPhase)
    {
        if (_isWorking && completedCurrentPhase)
        {
            _sessions++;
            _focusToday += _totalDuration;
            UpdateStats();
        }

        _isWorking = !_isWorking;

        if (_isWorking)
        {
            _isLongBreak = false;
            SetPhase(GetMinutes(WorkMinutes, 25), "Focus time", "Ready to focus");
        }
        else
        {
            var every = GetInt(LongBreakEvery, 4, 1, 12);
            _isLongBreak = _sessions > 0 && _sessions % every == 0;
            var minutes = _isLongBreak
                ? GetMinutes(LongBreakMinutes, 15)
                : GetMinutes(BreakMinutes, 5);
            SetPhase(minutes, _isLongBreak ? "Long break" : "Break time", _isLongBreak ? "Long break" : "Break time");
            WellbeingService.PlayNotificationSound();
        }

        UpdatePhaseDisplay();
    }

    private void SetPhase(int minutes, string runningLabel, string idleLabel)
    {
        _remaining = TimeSpan.FromMinutes(minutes);
        _totalDuration = _remaining;
        TimerLabel.Text = _isPomodoroRunning ? runningLabel : idleLabel;
        UpdateTimerDisplay();
    }

    private void ResetPomodoro()
    {
        _pomodoroTimer.Stop();
        _isPomodoroRunning = false;
        _isWorking = true;
        _isLongBreak = false;
        _remaining = TimeSpan.FromMinutes(GetMinutes(WorkMinutes, 25));
        _totalDuration = _remaining;
        SetStartPauseButton(true, "Start");
        TimerLabel.Text = "Ready to focus";
        UpdateTimerDisplay();
        UpdatePhaseDisplay();
        UpdateStats();
    }

    private void UpdateTimerDisplay()
    {
        TimerDisplay.Text = _remaining.TotalHours >= 1
            ? _remaining.ToString(@"h\:mm\:ss")
            : _remaining.ToString(@"mm\:ss");

        if (_totalDuration.TotalSeconds > 0)
        {
            var elapsed = _totalDuration - _remaining;
            TimerProgress.Value = Math.Clamp(elapsed.TotalSeconds / _totalDuration.TotalSeconds * 100, 0, 100);
        }

        TimerDisplay.Foreground = _isWorking
            ? FindBrush("HeaderColor")
            : FindBrush("PayloadColor");
    }

    private void UpdatePhaseDisplay()
    {
        PhaseBadgeText.Text = _isWorking ? "Focus" : _isLongBreak ? "Long Break" : "Break";
        PhaseBadgeText.Foreground = _isWorking ? FindBrush("HeaderColor") : FindBrush("PayloadColor");
        PhaseDetail.Text = _isWorking
            ? $"Next break after this block. Session {_sessions + 1}."
            : _isLongBreak
                ? "Step away properly. This is the real recovery block."
                : "Short reset. Move, blink, breathe, return.";
    }

    private void SetStartPauseButton(bool canStart, string? label = null)
    {
        StartPauseGlyph.Text = canStart ? "\uE768" : "\uE769";
        StartPauseText.Text = label ?? (canStart ? "Start" : "Pause");
    }

    private void UpdateStats()
    {
        SessionCount.Text = _sessions.ToString();
        FocusToday.Text = FormatFocus(_focusToday);
    }

    private void OnResetStatsClick(object sender, RoutedEventArgs e)
    {
        _sessions = 0;
        _focusToday = TimeSpan.Zero;
        UpdateStats();
        UpdatePhaseDisplay();
    }

    private static string FormatFocus(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(0, (int)Math.Round(span.TotalMinutes))}m";
    }

    private static int GetMinutes(TextBox box, int fallback)
        => GetInt(box, fallback, 1, 180);

    private static int GetInt(TextBox box, int fallback, int min, int max)
    {
        if (int.TryParse(box.Text, out var val) && val >= min && val <= max)
            return val;
        return fallback;
    }

    private void OnToggleReminderClick(object sender, RoutedEventArgs e)
    {
        if (_reminderTimer.IsEnabled)
        {
            _reminderTimer.Stop();
            ReminderToggleText.Text = "Enable";
            ReminderGlyph.Text = "\uE73E";
            ReminderStatus.Text = "Disabled";
            ReminderStatus.Foreground = FindBrush("TextSecondary");
        }
        else
        {
            var minutes = GetInt(ReminderInterval, 20, 1, 180);
            _reminderTimer.Interval = TimeSpan.FromMinutes(minutes);
            _reminderTimer.Start();
            ReminderToggleText.Text = "Disable";
            ReminderGlyph.Text = "\uE711";
            ReminderStatus.Text = $"Active. Next reminder in {minutes} minutes.";
            ReminderStatus.Foreground = FindBrush("SuccessText");
        }
    }

    private void OnReminderTick(object? sender, EventArgs e)
    {
        WellbeingService.PlayNotificationSound();

        var minutes = _reminderTimer.Interval.TotalMinutes;
        ReminderStatus.Text = $"Break active. Next in {minutes:0} min.";
        ReminderStatus.Foreground = FindBrush("PayloadColor");

        var breakSeconds = GetInt(BreakDuration, 20, 5, 300);
        var overlay = new BreakOverlayWindow(breakSeconds);
        overlay.ShowDialog();

        ReminderStatus.Text = $"Active. Next reminder in {minutes:0} minutes.";
        ReminderStatus.Foreground = FindBrush("SuccessText");
    }

    private void OnBreathStartPauseClick(object sender, RoutedEventArgs e)
    {
        if (_isBreathingRunning)
        {
            _breathTimer.Stop();
            _isBreathingRunning = false;
            BreathStartText.Text = "Resume";
            BreathInstruction.Text = "Paused";
        }
        else
        {
            _breathTimer.Start();
            _isBreathingRunning = true;
            BreathStartText.Text = "Pause";
            UpdateBreathDisplay();
        }
    }

    private void OnBreathResetClick(object sender, RoutedEventArgs e) => ResetBreathing();

    private void OnBreathTick(object? sender, EventArgs e)
    {
        _breathRemaining -= TimeSpan.FromSeconds(1);
        _breathStepRemaining--;

        if (_breathRemaining <= TimeSpan.Zero)
        {
            WellbeingService.PlayNotificationSound();
            ResetBreathing("Done. Nice reset.");
            return;
        }

        if (_breathStepRemaining <= 0)
        {
            _breathStepIndex = (_breathStepIndex + 1) % _breathSteps.Length;
            _breathStepRemaining = 4;
        }

        UpdateBreathDisplay();
    }

    private void ResetBreathing(string label = "Box breathing")
    {
        _breathTimer.Stop();
        _isBreathingRunning = false;
        _breathRemaining = TimeSpan.FromMinutes(1);
        _breathStepIndex = 0;
        _breathStepRemaining = 4;
        BreathStartText.Text = "Start";
        BreathInstruction.Text = label;
        UpdateBreathDisplay(false);
    }

    private void UpdateBreathDisplay(bool showStep = true)
    {
        BreathDisplay.Text = _breathRemaining.ToString(@"mm\:ss");
        BreathProgress.Value = (60 - _breathRemaining.TotalSeconds) / 60 * 100;
        if (showStep)
            BreathInstruction.Text = $"{_breathSteps[_breathStepIndex]} {_breathStepRemaining}";
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.White;

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["workMinutes"] = WorkMinutes.Text,
            ["breakMinutes"] = BreakMinutes.Text,
            ["longBreakMinutes"] = LongBreakMinutes.Text,
            ["longBreakEvery"] = LongBreakEvery.Text,
            ["sessions"] = _sessions.ToString(),
            ["focusTodaySeconds"] = ((int)_focusToday.TotalSeconds).ToString(),
            ["reminderInterval"] = ReminderInterval.Text,
            ["breakDuration"] = BreakDuration.Text
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("workMinutes", out var workMinutes)) WorkMinutes.Text = workMinutes;
        if (state.TryGetValue("breakMinutes", out var breakMinutes)) BreakMinutes.Text = breakMinutes;
        if (state.TryGetValue("longBreakMinutes", out var longBreakMinutes)) LongBreakMinutes.Text = longBreakMinutes;
        if (state.TryGetValue("longBreakEvery", out var longBreakEvery)) LongBreakEvery.Text = longBreakEvery;
        if (state.TryGetValue("reminderInterval", out var reminderInterval)) ReminderInterval.Text = reminderInterval;
        if (state.TryGetValue("breakDuration", out var breakDuration)) BreakDuration.Text = breakDuration;
        if (state.TryGetValue("sessions", out var sessions) && int.TryParse(sessions, out var parsedSessions)) _sessions = parsedSessions;
        if (state.TryGetValue("focusTodaySeconds", out var seconds) && int.TryParse(seconds, out var parsedSeconds))
            _focusToday = TimeSpan.FromSeconds(parsedSeconds);

        ResetPomodoro();
        UpdateStats();
    }
}

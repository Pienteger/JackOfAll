using System.Windows;
using System.Windows.Threading;
using JackOfAll.Lib.Settings;

namespace JackOfAll.Lib.Services;

public sealed class AutoSaveService : IDisposable
{
    private readonly Action _saveAction;
    private readonly Func<bool> _isDirty;
    private readonly Func<bool> _canSave;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public AutoSaveService(Action saveAction, Func<bool> isDirty, Func<bool> canSave)
    {
        _saveAction = saveAction;
        _isDirty = isDirty;
        _canSave = canSave;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            TrySave();
        };
    }

    public void NotifyChanged()
    {
        var settings = AppSettings.Current;
        if (settings is null || settings.AutoSaveMode != AutoSaveMode.AfterDelay) return;

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(settings.AutoSaveDelayMs, 200));
        _timer.Start();
    }

    public void NotifyFocusLost()
    {
        var settings = AppSettings.Current;
        if (settings is null || settings.AutoSaveMode != AutoSaveMode.OnFocusChange) return;

        TrySave();
    }

    public bool IsEnabled =>
        AppSettings.Current is { AutoSaveMode: not AutoSaveMode.Off };

    public bool TrySaveBeforeAction()
    {
        if (!IsEnabled) return false;
        if (_isDirty() && _canSave())
        {
            _timer.Stop();
            _saveAction();
            return true;
        }
        return !_isDirty();
    }

    private void TrySave()
    {
        if (_isDirty() && _canSave())
            _saveAction();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}

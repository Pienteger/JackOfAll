using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class ReminderToolView : UserControl
{
    private List<ReminderItem> _reminders = [];
    private bool _showAll = true;

    public ReminderToolView()
    {
        InitializeComponent();
        _reminders = ReminderService.Load();
        RefreshList();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddReminder();

    private void OnNewReminderKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddReminder();
    }

    private void AddReminder()
    {
        var text = NewReminderText.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var item = new ReminderItem { Text = text };
        if (DateTime.TryParse(DueDate.Text.Trim(), out var due))
            item.DueAt = due;

        _reminders.Insert(0, item);
        ReminderService.Save(_reminders);
        NewReminderText.Text = string.Empty;
        DueDate.Text = string.Empty;
        RefreshList();
    }

    private void OnToggleComplete(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
            ToggleComplete(id);
    }

    private void OnCheckboxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is Guid id)
            ToggleComplete(id);
    }

    private void ToggleComplete(Guid id)
    {
        var item = _reminders.FirstOrDefault(r => r.Id == id);
        if (item is null) return;
        item.IsCompleted = !item.IsCompleted;
        ReminderService.Save(_reminders);
        RefreshList();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid id)
        {
            _reminders.RemoveAll(r => r.Id == id);
            ReminderService.Save(_reminders);
            RefreshList();
        }
    }

    private void OnShowAll(object sender, RoutedEventArgs e) { _showAll = true; RefreshList(); }
    private void OnShowActive(object sender, RoutedEventArgs e) { _showAll = false; RefreshList(); }

    private void OnClearCompleted(object sender, RoutedEventArgs e)
    {
        _reminders.RemoveAll(r => r.IsCompleted);
        ReminderService.Save(_reminders);
        RefreshList();
    }

    private void RefreshList()
    {
        var visible = _showAll ? _reminders : _reminders.Where(r => !r.IsCompleted).ToList();
        ReminderList.ItemsSource = null;
        ReminderList.ItemsSource = visible;
        var active = _reminders.Count(r => !r.IsCompleted);
        CountText.Text = $"{active} active, {_reminders.Count} total";
    }
}

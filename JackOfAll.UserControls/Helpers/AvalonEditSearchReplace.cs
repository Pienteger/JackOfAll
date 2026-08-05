using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace JackOfAll.UserControls.Helpers;

public static class AvalonEditSearchReplace
{
    public static void Attach(TextEditor editor)
    {
        var controller = new Controller(editor);
        editor.PreviewKeyDown += controller.OnPreviewKeyDown;
        editor.Unloaded += (_, _) => controller.Close();

        var menu = editor.ContextMenu ?? editor.TextArea.ContextMenu ?? new ContextMenu();
        if (menu.Items.Count == 0)
        {
            AddCommandItem(menu, "Cut", ApplicationCommands.Cut, editor.TextArea);
            AddCommandItem(menu, "Copy", ApplicationCommands.Copy, editor.TextArea);
            AddCommandItem(menu, "Paste", ApplicationCommands.Paste, editor.TextArea);
            menu.Items.Add(new Separator());
            AddCommandItem(menu, "Select All", ApplicationCommands.SelectAll, editor.TextArea);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        AddMenuItem(menu, "Find", "Ctrl+F", () => controller.Show(false), () => editor.Text.Length > 0);
        AddMenuItem(menu, "Replace", "Ctrl+H", () => controller.Show(true), () => !editor.IsReadOnly);
        editor.ContextMenu = menu;
        editor.TextArea.ContextMenu = menu;
    }

    private static void AddCommandItem(
        ContextMenu menu, string header, ICommand command, IInputElement target) =>
        menu.Items.Add(new MenuItem { Header = header, Command = command, CommandTarget = target });

    private static void AddMenuItem(
        ContextMenu menu, string header, string gesture, Action execute, Func<bool> canExecute)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture,
            Tag = canExecute
        };
        item.Click += (_, _) => execute();
        menu.Items.Add(item);
    }

    private sealed class Controller(TextEditor editor)
    {
        private AvalonEditFindReplaceWindow? _window;

        public void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H)
            {
                Show(e.Key == Key.H);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F3 && _window is not null)
            {
                _window.FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
            }
        }

        public void Show(bool replace)
        {
            _window ??= CreateWindow();
            _window.ShowReplace = replace && !editor.IsReadOnly;
            if (!_window.IsVisible) _window.Show();
            _window.Activate();
            _window.FocusFind();
        }

        public void Close() => _window?.Close();

        private AvalonEditFindReplaceWindow CreateWindow()
        {
            var window = new AvalonEditFindReplaceWindow(editor)
            {
                Owner = Window.GetWindow(editor)
            };
            window.Closed += (_, _) => _window = null;
            return window;
        }
    }
}

public partial class AvalonEditFindReplaceWindow : Window
{
    private readonly TextEditor _editor;

    public AvalonEditFindReplaceWindow(TextEditor editor)
    {
        InitializeComponent();
        _editor = editor;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        var selection = editor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selection) && selection.Length <= 200 && !selection.Contains('\n'))
            FindBox.Text = selection;
    }

    public bool ShowReplace
    {
        set
        {
            ReplaceRow.Height = value ? GridLength.Auto : new GridLength(0);
            ReplaceLabel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ReplaceBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ReplaceButtons.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Title = value ? "Find and Replace" : "Find";
        }
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    public void FocusReplace()
    {
        ReplaceBox.Focus();
        ReplaceBox.SelectAll();
    }

    public void FindNext(bool backwards = false)
    {
        var query = FindBox.Text;
        if (string.IsNullOrEmpty(query)) return;

        var text = _editor.Text;
        if (text.Length == 0)
        {
            StatusText.Text = "No matches";
            return;
        }

        var comparison = MatchCaseBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int offset;
        if (backwards)
        {
            offset = _editor.SelectionStart > 0
                ? text.LastIndexOf(query, _editor.SelectionStart - 1, comparison)
                : -1;
            if (offset < 0) offset = text.LastIndexOf(query, comparison);
        }
        else
        {
            var start = _editor.SelectionStart + _editor.SelectionLength;
            offset = text.IndexOf(query, start, comparison);
            if (offset < 0) offset = text.IndexOf(query, comparison);
        }

        if (offset < 0)
        {
            StatusText.Text = "No matches";
            return;
        }

        _editor.Select(offset, query.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(offset).LineNumber);
        StatusText.Text = GetMatchStatus(text, query, offset, comparison);
    }

    private static string GetMatchStatus(
        string text, string query, int selectedOffset, StringComparison comparison)
    {
        var count = 0;
        var selectedIndex = 0;
        for (var offset = 0; offset <= text.Length - query.Length;)
        {
            var match = text.IndexOf(query, offset, comparison);
            if (match < 0) break;
            count++;
            if (match <= selectedOffset) selectedIndex = count;
            offset = match + query.Length;
        }
        return $"{selectedIndex} of {count} matches";
    }

    private void OnNextClick(object sender, RoutedEventArgs e) => FindNext();
    private void OnPreviousClick(object sender, RoutedEventArgs e) => FindNext(true);

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (_editor.IsReadOnly) return;
        var comparison = MatchCaseBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(_editor.SelectedText, FindBox.Text, comparison))
            _editor.Document.Replace(_editor.SelectionStart, _editor.SelectionLength, ReplaceBox.Text);
        FindNext();
    }

    private void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        if (_editor.IsReadOnly || string.IsNullOrEmpty(FindBox.Text)) return;
        var comparison = MatchCaseBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var count = 0;
        var offset = 0;
        using (_editor.Document.RunUpdate())
        {
            while (offset <= _editor.Document.TextLength)
            {
                var match = _editor.Text.IndexOf(FindBox.Text, offset, comparison);
                if (match < 0) break;
                _editor.Document.Replace(match, FindBox.Text.Length, ReplaceBox.Text);
                offset = match + ReplaceBox.Text.Length;
                count++;
            }
        }
        StatusText.Text = $"Replaced {count} occurrence{(count == 1 ? "" : "s")}";
    }

    private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
        {
            ShowReplace = !_editor.IsReadOnly;
            if (_editor.IsReadOnly)
                FocusFind();
            else
                FocusReplace();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            ShowReplace = false;
            FocusFind();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }
}

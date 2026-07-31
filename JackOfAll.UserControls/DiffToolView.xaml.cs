using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using JackOfAll.Lib;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Helpers;

namespace JackOfAll.UserControls;

public partial class DiffToolView : UserControl, IStatefulTool
{
    private List<DiffLine> _lastLines = [];

    public DiffToolView()
    {
        InitializeComponent();
        SyntaxHelper.EnsureRegistered();

        ConfigureEditorTheme(LeftBox);
        ConfigureEditorTheme(RightBox);
        AttachEditorContextMenu(LeftBox);
        AttachEditorContextMenu(RightBox);
        ApplySyntaxHighlighting();

        LeftBox.TextChanged += OnInputChanged;
        RightBox.TextChanged += OnInputChanged;
        Loaded += (_, _) => Compare();
        ThemeNotifier.ThemeChanged += OnThemeChanged;
    }


    private static void AttachEditorContextMenu(TextEditor editor)
    {
        editor.ContextMenu = BuildEditorContextMenu(editor);
        editor.TextArea.ContextMenu = editor.ContextMenu;
    }

    private static ContextMenu BuildEditorContextMenu(TextEditor editor)
    {
        var menu = new ContextMenu();
        AddEditorMenuItem(menu, "Cut", () => editor.Cut(), () => !editor.IsReadOnly && !string.IsNullOrEmpty(editor.SelectedText));
        AddEditorMenuItem(menu, "Copy", () => editor.Copy(), () => !string.IsNullOrEmpty(editor.SelectedText));
        AddEditorMenuItem(menu, "Paste", () => editor.Paste(), () => !editor.IsReadOnly && Clipboard.ContainsText());
        menu.Items.Add(new Separator());
        AddEditorMenuItem(menu, "Select All", () => editor.SelectAll(), () => editor.Text.Length > 0);
        menu.Opened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsEnabled = item.Tag is Func<bool> canExecute && canExecute();
        };
        return menu;
    }

    private static void AddEditorMenuItem(ContextMenu menu, string header, Action execute, Func<bool> canExecute)
    {
        var item = new MenuItem { Header = header, Tag = canExecute };
        item.Click += (_, _) => execute();
        menu.Items.Add(item);
    }
    private void OnThemeChanged()
    {
        ConfigureEditorTheme(LeftBox);
        ConfigureEditorTheme(RightBox);
        ApplySyntaxHighlighting();
    }

    private void ApplySyntaxHighlighting()
    {
        var highlighting = JsonModeRadio.IsChecked == true
            ? SyntaxHelper.ForExtension(".json")
            : null;

        LeftBox.SyntaxHighlighting = highlighting;
        RightBox.SyntaxHighlighting = highlighting;
    }

    private static void ConfigureEditorTheme(TextEditor editor)
    {
        var dark = SyntaxHelper.IsDarkTheme();
        editor.Options.EnableHyperlinks = false;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 2;
        editor.TextArea.SelectionForeground = null;

        if (dark)
        {
            editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), 1);
            editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x26, 0x4F, 0x78));
        }
        else
        {
            editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00)), 1);
            editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x33, 0x99, 0xFF));
        }
    }

    private void OnCompareClick(object sender, RoutedEventArgs e) => Compare();

    private void OnInputChanged(object? sender, EventArgs e) => Compare();

    private void OnOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ApplySyntaxHighlighting();
        Compare();
    }

    private void Compare()
    {
        if (!IsLoaded)
            return;

        if (string.IsNullOrEmpty(LeftBox.Text) && string.IsNullOrEmpty(RightBox.Text))
        {
            _lastLines = [];
            DiffList.ItemsSource = null;
            StatusText.Text = "Paste content on both sides to compare.";
            StatusText.Foreground = FindBrush("TextDimmed");
            return;
        }

        var mode = JsonModeRadio.IsChecked == true ? DiffMode.JsonSemantic : DiffMode.Text;
        var result = DiffService.Compare(
            LeftBox.Text,
            RightBox.Text,
            mode,
            IgnoreWhitespaceCheck.IsChecked == true);

        if (!result.Success)
        {
            _lastLines = [];
            DiffList.ItemsSource = null;
            StatusText.Text = result.Error ?? result.Summary;
            StatusText.Foreground = FindBrush("ErrorText");
            return;
        }

        _lastLines = result.Lines;
        DiffList.ItemsSource = result.Lines.Select(ToViewLine).ToList();
        StatusText.Text = mode == DiffMode.JsonSemantic
            ? $"{result.Summary} Object properties ignored for ordering."
            : result.Summary;
        StatusText.Foreground = result.Summary.StartsWith("No ", StringComparison.Ordinal)
            ? FindBrush("SuccessText")
            : FindBrush("TextSecondary");
    }

    private DiffViewLine ToViewLine(DiffLine line)
    {
        return line.Kind switch
        {
            DiffLineKind.Added => new DiffViewLine("+", line.Text, FindBrush("SuccessText"), FindBrush("SuccessBackground")),
            DiffLineKind.Removed => new DiffViewLine("-", line.Text, FindBrush("ErrorText"), FindBrush("ErrorBackground")),
            _ => new DiffViewLine(" ", line.Text, FindBrush("TextPrimary"), Brushes.Transparent)
        };
    }

    private void OnSwapClick(object sender, RoutedEventArgs e)
    {
        (LeftBox.Text, RightBox.Text) = (RightBox.Text, LeftBox.Text);
        Compare();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        LeftBox.Text = string.Empty;
        RightBox.Text = string.Empty;
        Compare();
    }

    private void OnCopyDiffClick(object sender, RoutedEventArgs e)
    {
        if (_lastLines.Count > 0)
            Clipboard.SetText(DiffService.ToUnifiedDiff(_lastLines));
    }

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public Dictionary<string, string> SaveState()
    {
        return new Dictionary<string, string>
        {
            ["left"] = LeftBox.Text,
            ["right"] = RightBox.Text,
            ["mode"] = JsonModeRadio.IsChecked == true ? "json" : "text",
            ["ignoreWhitespace"] = (IgnoreWhitespaceCheck.IsChecked == true).ToString()
        };
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("mode", out var mode))
        {
            JsonModeRadio.IsChecked = mode == "json";
            TextModeRadio.IsChecked = mode != "json";
        }

        if (state.TryGetValue("ignoreWhitespace", out var ignoreWhitespace))
            IgnoreWhitespaceCheck.IsChecked = ignoreWhitespace == "True";

        if (state.TryGetValue("left", out var left))
            LeftBox.Text = left;

        if (state.TryGetValue("right", out var right))
            RightBox.Text = right;

        ApplySyntaxHighlighting();
        Compare();
    }

    private sealed record DiffViewLine(string Prefix, string Text, Brush Foreground, Brush Background);
}

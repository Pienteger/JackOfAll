using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class NotepadToolView : UserControl, IStatefulTool
{
    private string? _currentPath;
    private bool _isDirty;
    private bool _suppressFileSelection;
    private FileMode _fileMode = FileMode.Markdown;

    private enum FileMode { Markdown, Text, Csv, Tsv, Json }

    private static readonly string[] SupportedExtensions =
        [".md", ".markdown", ".txt", ".csv", ".tsv", ".json",
         ".xml", ".yaml", ".yml", ".log", ".ini", ".cfg", ".conf", ".toml"];

    public NotepadToolView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncFilePanel();
        EnvironmentContext.Changed += SyncFilePanel;
    }

    // ── File mode detection ───────────────────────────────────────

    private static FileMode DetectMode(string? path)
    {
        if (path is null) return FileMode.Markdown;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => FileMode.Markdown,
            ".csv" => FileMode.Csv,
            ".tsv" => FileMode.Tsv,
            ".json" => FileMode.Json,
            _ => FileMode.Text
        };
    }

    private void ApplyFileMode(FileMode mode)
    {
        _fileMode = mode;

        HideSecondaryPanel();

        PreviewToggle.Visibility = mode == FileMode.Markdown ? Visibility.Visible : Visibility.Collapsed;
        CsvPreviewToggle.Visibility = mode is FileMode.Csv or FileMode.Tsv ? Visibility.Visible : Visibility.Collapsed;

        var jsonVis = mode == FileMode.Json ? Visibility.Visible : Visibility.Collapsed;
        BtnJsonFormat.Visibility = jsonVis;
        BtnJsonMinify.Visibility = jsonVis;
        BtnJsonEscape.Visibility = jsonVis;
        BtnJsonUnescape.Visibility = jsonVis;

        if (PreviewToggle.IsChecked == true && mode != FileMode.Markdown)
            PreviewToggle.IsChecked = false;
        if (CsvPreviewToggle.IsChecked == true && mode is not (FileMode.Csv or FileMode.Tsv))
            CsvPreviewToggle.IsChecked = false;
    }

    // ── Environment-driven file panel ─────────────────────────────

    private void SyncFilePanel()
    {
        if (!IsLoaded) return;
        var env = EnvironmentContext.Current;
        if (env is not null && Directory.Exists(env.FolderPath))
        {
            ShowFilePanel();
            RefreshFileList();
        }
        else
        {
            HideFilePanel();
        }
    }

    private void ShowFilePanel()
    {
        FilePanelColumn.Width = new GridLength(220);
        FilePanelSplitterColumn.Width = new GridLength(6);
        FilePanel.Visibility = Visibility.Visible;
        FilePanelSplitter.Visibility = Visibility.Visible;
    }

    private void HideFilePanel()
    {
        FilePanelColumn.Width = new GridLength(0);
        FilePanelSplitterColumn.Width = new GridLength(0);
        FilePanel.Visibility = Visibility.Collapsed;
        FilePanelSplitter.Visibility = Visibility.Collapsed;
    }

    private void RefreshFileList()
    {
        var env = EnvironmentContext.Current;
        if (env is null || !Directory.Exists(env.FolderPath))
        {
            FileList.ItemsSource = null;
            FilePanelEmpty.Text = "Folder not found";
            FilePanelEmpty.Visibility = Visibility.Visible;
            return;
        }

        var files = Directory.GetFiles(env.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new FileEntry(Path.GetFileName(f), f))
            .ToList();

        FileList.ItemsSource = files;
        FilePanelEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilePanelEmpty.Text = "No files yet";

        if (_currentPath is not null)
        {
            var match = files.FirstOrDefault(f => f.FullPath.Equals(_currentPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _suppressFileSelection = true;
                FileList.SelectedItem = match;
                _suppressFileSelection = false;
            }
        }
    }

    private void OnRefreshFiles(object sender, RoutedEventArgs e) => RefreshFileList();

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFileSelection) return;
        if (FileList.SelectedItem is not FileEntry entry) return;

        if (_isDirty && _currentPath != entry.FullPath)
        {
            var save = MessageBox.Show("Save changes before switching?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (save == MessageBoxResult.Cancel)
            {
                _suppressFileSelection = true;
                if (_currentPath is not null)
                {
                    var prev = (FileList.ItemsSource as List<FileEntry>)?.FirstOrDefault(f =>
                        f.FullPath.Equals(_currentPath, StringComparison.OrdinalIgnoreCase));
                    FileList.SelectedItem = prev;
                }
                _suppressFileSelection = false;
                return;
            }
            if (save == MessageBoxResult.Yes)
                SaveCurrentFile();
        }

        OpenFile(entry.FullPath);
    }

    private void OpenFile(string path)
    {
        _currentPath = path;
        EditorBox.Text = File.ReadAllText(path);
        _isDirty = false;
        FileTitle.Text = Path.GetFileName(path);
        ApplyFileMode(DetectMode(path));
        UpdateStatus();
    }

    private void SaveCurrentFile()
    {
        if (_currentPath is null) return;
        File.WriteAllText(_currentPath, EditorBox.Text);
        _isDirty = false;
        UpdateStatus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
        UpdateStatus();
        if (PreviewToggle.IsChecked == true && _fileMode == FileMode.Markdown)
            UpdateMarkdownPreview();
        if (CsvPreviewToggle.IsChecked == true && _fileMode is FileMode.Csv or FileMode.Tsv)
            UpdateCsvPreview();
    }

    // ── File operations ───────────────────────────────────────────

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var save = MessageBox.Show("Save changes first?", "Unsaved Changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (save == MessageBoxResult.Cancel) return;
            if (save == MessageBoxResult.Yes)
            {
                OnSaveClick(sender, e);
                if (_isDirty) return;
            }
        }

        EditorBox.Text = string.Empty;
        _currentPath = null;
        _isDirty = false;
        FileTitle.Text = "Untitled";
        ApplyFileMode(FileMode.Text);
        UpdateStatus();

        _suppressFileSelection = true;
        FileList.SelectedItem = null;
        _suppressFileSelection = false;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var initialDir = EnvironmentContext.Current?.FolderPath;

        var dlg = new OpenFileDialog
        {
            Filter = "All supported|*.md;*.markdown;*.txt;*.csv;*.tsv;*.json;*.xml;*.yaml;*.yml|Markdown|*.md;*.markdown|Text|*.txt|CSV/TSV|*.csv;*.tsv|JSON|*.json|All files|*.*",
            DefaultExt = ".txt"
        };

        if (initialDir is not null && Directory.Exists(initialDir))
            dlg.InitialDirectory = initialDir;

        if (dlg.ShowDialog() == true)
        {
            OpenFile(dlg.FileName);
            RefreshFileList();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null)
        {
            OnSaveAsClick(sender, e);
            return;
        }

        File.WriteAllText(_currentPath, EditorBox.Text);
        _isDirty = false;
        UpdateStatus();
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var env = EnvironmentContext.Current;
        string defaultDir;
        if (env is not null && Directory.Exists(env.FolderPath))
        {
            defaultDir = env.FolderPath;
        }
        else
        {
            defaultDir = Path.Combine(Lib.AppPaths.Root, "notes");
            Directory.CreateDirectory(defaultDir);
        }

        var dlg = new SaveFileDialog
        {
            Filter = "All supported|*.md;*.txt;*.csv;*.tsv;*.json|Markdown|*.md|Text|*.txt|CSV|*.csv|TSV|*.tsv|JSON|*.json|All files|*.*",
            DefaultExt = ".txt",
            FileName = _currentPath is not null ? Path.GetFileName(_currentPath) : "Untitled.txt",
            InitialDirectory = _currentPath is not null ? Path.GetDirectoryName(_currentPath)! : defaultDir
        };

        if (dlg.ShowDialog() == true)
        {
            _currentPath = dlg.FileName;
            File.WriteAllText(_currentPath, EditorBox.Text);
            _isDirty = false;
            FileTitle.Text = Path.GetFileName(_currentPath);
            ApplyFileMode(DetectMode(_currentPath));
            UpdateStatus();
            RefreshFileList();
        }
    }

    // ── Markdown preview ──────────────────────────────────────────

    private void OnPreviewToggle(object sender, RoutedEventArgs e)
    {
        if (PreviewToggle.IsChecked == true)
        {
            ShowSecondaryPanel();
            MarkdownPreviewPanel.Visibility = Visibility.Visible;
            UpdateMarkdownPreview();
        }
        else
        {
            HideSecondaryPanel();
        }
    }

    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html>
        <head>
        <style>
            body {
                font-family: 'Segoe UI', sans-serif;
                font-size: 14px;
                line-height: 1.6;
                color: #dcdcdc;
                background: #1e1e1e;
                padding: 16px;
                margin: 0;
            }
            h1, h2, h3, h4 { color: #569cd6; margin-top: 1em; }
            code {
                background: #2d2d2d;
                padding: 2px 6px;
                border-radius: 3px;
                font-family: 'Cascadia Code', Consolas, monospace;
                font-size: 13px;
            }
            pre {
                background: #2d2d2d;
                padding: 12px;
                border-radius: 6px;
                overflow-x: auto;
            }
            pre code { padding: 0; background: none; }
            a { color: #569cd6; }
            blockquote {
                border-left: 3px solid #569cd6;
                margin-left: 0;
                padding-left: 12px;
                color: #9cdcfe;
            }
            table { border-collapse: collapse; width: 100%; }
            th, td { border: 1px solid #3d3d3d; padding: 6px 10px; text-align: left; }
            th { background: #2d2d2d; }
            hr { border: none; border-top: 1px solid #3d3d3d; }
            img { max-width: 100%; }
        </style>
        </head>
        <body>{{CONTENT}}</body>
        </html>
        """;

    private void UpdateMarkdownPreview()
    {
        var html = MarkdownService.ToHtml(EditorBox.Text);
        PreviewBrowser.NavigateToString(HtmlTemplate.Replace("{{CONTENT}}", html));
    }

    // ── CSV/TSV table preview ─────────────────────────────────────

    private void OnCsvPreviewToggle(object sender, RoutedEventArgs e)
    {
        if (CsvPreviewToggle.IsChecked == true)
        {
            ShowSecondaryPanel();
            CsvPreviewPanel.Visibility = Visibility.Visible;
            UpdateCsvPreview();
        }
        else
        {
            HideSecondaryPanel();
        }
    }

    private void UpdateCsvPreview()
    {
        try
        {
            var separator = _fileMode == FileMode.Tsv ? '\t' : ',';
            var lines = EditorBox.Text.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length == 0) { CsvDataGrid.ItemsSource = null; return; }

            var headers = SplitCsvLine(lines[0], separator);
            var dt = new DataTable();
            foreach (var h in headers)
                dt.Columns.Add(h.Trim());

            for (int i = 1; i < lines.Length; i++)
            {
                var cells = SplitCsvLine(lines[i], separator);
                var row = dt.NewRow();
                for (int c = 0; c < Math.Min(cells.Length, dt.Columns.Count); c++)
                    row[c] = cells[c].Trim();
                dt.Rows.Add(row);
            }

            CsvDataGrid.ItemsSource = dt.DefaultView;
            SetStatus($"{dt.Rows.Count} rows, {dt.Columns.Count} columns", "SurfaceBackground", "TextSecondary");
        }
        catch (Exception ex)
        {
            SetStatus($"Parse error: {ex.Message}", "ErrorBackground", "ErrorText");
        }
    }

    private static string[] SplitCsvLine(string line, char separator)
    {
        if (separator == '\t') return line.Split('\t');

        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == separator && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else
            {
                current += c;
            }
        }
        result.Add(current);
        return result.ToArray();
    }

    // ── JSON tools ────────────────────────────────────────────────

    private void OnJsonFormatClick(object sender, RoutedEventArgs e)
    {
        var input = EditorBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Format(input);
        if (result is not null)
        {
            EditorBox.Text = result;
            SetStatus("Formatted.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void OnJsonMinifyClick(object sender, RoutedEventArgs e)
    {
        var input = EditorBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Minify(input);
        if (result is not null)
        {
            ShowJsonOutput(result);
            SetStatus("Minified → output panel.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void OnJsonEscapeClick(object sender, RoutedEventArgs e)
    {
        var input = EditorBox.Text;
        if (string.IsNullOrEmpty(input)) return;

        ShowJsonOutput(JsonService.Escape(input));
        SetStatus("Escaped → output panel.", "SuccessBackground", "SuccessText");
    }

    private void OnJsonUnescapeClick(object sender, RoutedEventArgs e)
    {
        var input = EditorBox.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;

        var (result, error) = JsonService.Unescape(input);
        if (result is not null)
        {
            ShowJsonOutput(result);
            SetStatus("Unescaped → output panel.", "SuccessBackground", "SuccessText");
        }
        else
        {
            SetStatus(error!, "ErrorBackground", "ErrorText");
        }
    }

    private void ShowJsonOutput(string text)
    {
        ShowSecondaryPanel();
        JsonOutputPanel.Visibility = Visibility.Visible;
        JsonOutputBox.Text = text;
    }

    private void OnCopyOutputClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(JsonOutputBox.Text))
        {
            Clipboard.SetText(JsonOutputBox.Text);
            SetStatus("Copied to clipboard.", "SuccessBackground", "SuccessText");
        }
    }

    // ── Ctrl+Wheel font zoom ─────────────────────────────────────

    private void OnEditorMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        AdjustEditorFontSize(e.Delta > 0 ? 1 : -1);
    }

    private static void AdjustEditorFontSize(int direction)
    {
        var settings = AppSettings.Current;
        if (settings is null) return;

        var newSize = Math.Clamp(settings.EditorFontSize + direction, 10, 32);
        if (Math.Abs(newSize - settings.EditorFontSize) < 0.1) return;

        settings.EditorFontSize = newSize;
        Application.Current.Resources["EditorFontSize"] = newSize;
        settings.Save();
    }

    // ── File panel: New / Rename / Delete ─────────────────────────

    private void OnNewFileClick(object sender, RoutedEventArgs e)
    {
        var env = EnvironmentContext.Current;
        if (env is null || !Directory.Exists(env.FolderPath)) return;

        var dlg = new InputDialog("New File", "File name (e.g. notes.txt):");
        var owner = Window.GetWindow(this);
        if (owner is not null) dlg.Owner = owner;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

        var fileName = dlg.ResultText.Trim();
        var filePath = Path.Combine(env.FolderPath, fileName);
        if (File.Exists(filePath))
        {
            MessageBox.Show($"'{fileName}' already exists.", "File Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        File.WriteAllText(filePath, string.Empty);
        RefreshFileList();
        OpenFile(filePath);
    }

    private void OnRenameFileClick(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry entry) return;

        var dlg = new InputDialog("Rename File", "New name:");
        var owner = Window.GetWindow(this);
        if (owner is not null) dlg.Owner = owner;
        dlg.InputBox.Text = entry.Name;
        dlg.InputBox.SelectAll();
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

        var newName = dlg.ResultText.Trim();
        var dir = Path.GetDirectoryName(entry.FullPath)!;
        var newPath = Path.Combine(dir, newName);

        if (File.Exists(newPath))
        {
            MessageBox.Show($"'{newName}' already exists.", "File Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        File.Move(entry.FullPath, newPath);

        if (_currentPath is not null &&
            _currentPath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentPath = newPath;
            FileTitle.Text = newName;
            ApplyFileMode(DetectMode(newPath));
        }

        RefreshFileList();
    }

    private void OnDeleteFileClick(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry entry) return;

        var result = MessageBox.Show($"Delete '{entry.Name}'?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        File.Delete(entry.FullPath);

        if (_currentPath is not null &&
            _currentPath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentPath = null;
            EditorBox.Text = string.Empty;
            _isDirty = false;
            FileTitle.Text = "Untitled";
            ApplyFileMode(FileMode.Text);
        }

        RefreshFileList();
        UpdateStatus();
    }

    // ── Secondary panel management ────────────────────────────────

    private void ShowSecondaryPanel()
    {
        MarkdownPreviewPanel.Visibility = Visibility.Collapsed;
        CsvPreviewPanel.Visibility = Visibility.Collapsed;
        JsonOutputPanel.Visibility = Visibility.Collapsed;

        SplitterColumn.Width = new GridLength(6);
        SecondaryColumn.Width = new GridLength(1, GridUnitType.Star);
        Splitter.Visibility = Visibility.Visible;
    }

    private void HideSecondaryPanel()
    {
        MarkdownPreviewPanel.Visibility = Visibility.Collapsed;
        CsvPreviewPanel.Visibility = Visibility.Collapsed;
        JsonOutputPanel.Visibility = Visibility.Collapsed;

        SplitterColumn.Width = new GridLength(0);
        SecondaryColumn.Width = new GridLength(0);
        Splitter.Visibility = Visibility.Collapsed;
    }

    // ── Status ────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        var lines = EditorBox.Text.Split('\n').Length;
        var chars = EditorBox.Text.Length;
        var dirty = _isDirty ? " (modified)" : "";
        var envLabel = EnvironmentContext.Current is not null ? $"  [{EnvironmentContext.Current.Name}]" : "";
        var modeLabel = _fileMode switch
        {
            FileMode.Json => "  JSON",
            FileMode.Csv => "  CSV",
            FileMode.Tsv => "  TSV",
            FileMode.Markdown => "  Markdown",
            _ => ""
        };
        SetStatus($"Lines: {lines}  Chars: {chars}{dirty}{modeLabel}{envLabel}", "SurfaceBackground", "TextSecondary");
    }

    private void SetStatus(string message, string backgroundKey, string foregroundKey)
    {
        StatusText.Text = message;
        StatusBorder.Background = FindBrush(backgroundKey);
        StatusText.Foreground = FindBrush(foregroundKey);
    }

    private Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    // ── IStatefulTool ─────────────────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["text"] = EditorBox.Text,
            ["title"] = FileTitle.Text,
            ["preview"] = (PreviewToggle.IsChecked == true).ToString(),
            ["mode"] = _fileMode.ToString()
        };
        if (_currentPath is not null)
            state["path"] = _currentPath;
        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("path", out var path) && File.Exists(path))
        {
            OpenFile(path);
        }
        else
        {
            if (state.TryGetValue("text", out var text))
                EditorBox.Text = text;
            if (state.TryGetValue("title", out var title))
                FileTitle.Text = title;
            if (state.TryGetValue("mode", out var modeStr) && Enum.TryParse<FileMode>(modeStr, out var mode))
                ApplyFileMode(mode);
        }

        if (state.TryGetValue("preview", out var preview) && preview == "True" && _fileMode == FileMode.Markdown)
            PreviewToggle.IsChecked = true;

        _isDirty = false;
        UpdateStatus();
        SyncFilePanel();
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackOfAll.Lib;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Helpers;

namespace JackOfAll.UserControls;

public partial class CodeRunnerToolView : UserControl, IStatefulTool
{
    private string _language = "csharp";
    private readonly string _tempDir;
    private readonly AutoSaveService _autoSave;
    private string? _currentFilePath;
    private bool _isDirty;
    private bool _suppressFileSelection;

    private static readonly string[] CodeExtensions = [".cs", ".js", ".py"];

    private static readonly Dictionary<string, string> Templates = new()
    {
        ["csharp"] = """
                     Console.WriteLine("Hello from C#");
                     """,
        ["javascript"] = """
                         console.log("Hello from JavaScript");
                         """,
        ["python"] = """
                     print("Hello from Python")
                     """
    };

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["csharp"] = ".cs",
        ["javascript"] = ".js",
        ["python"] = ".py"
    };

    private static readonly Dictionary<string, string> ExtensionToLanguage = new()
    {
        [".cs"] = "csharp",
        [".js"] = "javascript",
        [".py"] = "python"
    };

    private static readonly Dictionary<string, string> RuntimeLabels = new()
    {
        ["csharp"] = "dotnet run",
        ["javascript"] = "bun / node",
        ["python"] = "python / uv"
    };

    public CodeRunnerToolView()
    {
        InitializeComponent();
        SyntaxHelper.EnsureRegistered();
        _tempDir = Path.Combine(AppPaths.Root, "code-runner");
        Directory.CreateDirectory(_tempDir);

        ConfigureEditorTheme();
        CodeEditor.TextChanged += OnCodeChanged;
        CodeEditor.PreviewKeyDown += OnCodeKeyDown;
        ApplySyntaxHighlighting();

        _autoSave = new AutoSaveService(
            () => SaveCurrentFile(),
            () => _isDirty,
            () => _currentFilePath is not null);

        Loaded += (_, _) => SyncFilePanel();
        EnvironmentContext.Changed += SyncFilePanel;
        CodeEditor.Text = Templates["csharp"];
        ThemeNotifier.ThemeChanged += OnThemeChanged;
        LostFocus += (_, _) => _autoSave.NotifyFocusLost();
    }

    private void OnThemeChanged()
    {
        ConfigureEditorTheme();
        ApplySyntaxHighlighting();
    }

    private void ConfigureEditorTheme()
    {
        var dark = SyntaxHelper.IsDarkTheme();
        CodeEditor.Options.EnableHyperlinks = false;
        CodeEditor.Options.HighlightCurrentLine = true;
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.IndentationSize = 4;
        CodeEditor.TextArea.SelectionForeground = null;

        if (dark)
        {
            CodeEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            CodeEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            CodeEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), 1);
            CodeEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x26, 0x4F, 0x78));
        }
        else
        {
            CodeEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            CodeEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            CodeEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00)), 1);
            CodeEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x33, 0x99, 0xFF));
        }
    }

    private void ApplySyntaxHighlighting()
    {
        CodeEditor.SyntaxHighlighting = SyntaxHelper.ForLanguage(_language);
    }

    // ── Environment-driven file panel ─────────────────────────────

    private void SyncFilePanel()
    {
        if (!IsLoaded) return;
        var env = EnvironmentContext.Current;
        if (env is not null && Directory.Exists(env.FolderPath))
        {
            ShowFilePanel(true);
            RefreshFileList();
        }
        else
        {
            ShowFilePanel(false);
        }
    }

    private void ShowFilePanel(bool show)
    {
        if (show)
        {
            FilePanelColumn.Width = new GridLength(200);
            FilePanelSplitterColumn.Width = new GridLength(4);
            FilePanel.Visibility = Visibility.Visible;
        }
        else
        {
            FilePanelColumn.Width = new GridLength(0);
            FilePanelSplitterColumn.Width = new GridLength(0);
            FilePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshFileList()
    {
        var env = EnvironmentContext.Current;
        if (env is null || !Directory.Exists(env.FolderPath))
        {
            FileList.ItemsSource = null;
            return;
        }

        var files = Directory.EnumerateFiles(env.FolderPath)
            .Where(f => CodeExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => Path.GetFileName(f))
            .Select(f => new FileEntry(Path.GetFileName(f), f))
            .ToList();

        _suppressFileSelection = true;
        FileList.ItemsSource = files;

        if (_currentFilePath is not null)
        {
            var match = files.FindIndex(f => f.FullPath == _currentFilePath);
            if (match >= 0) FileList.SelectedIndex = match;
        }
        _suppressFileSelection = false;
    }

    private void OnRefreshFiles(object sender, RoutedEventArgs e) => RefreshFileList();

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFileSelection || !IsLoaded) return;
        if (FileList.SelectedItem is not FileEntry entry) return;

        if (_isDirty)
        {
            if (_autoSave.IsEnabled)
            {
                _autoSave.TrySaveBeforeAction();
            }
            else
            {
                var answer = MessageBox.Show("Save changes before switching files?",
                    "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel)
                {
                    _suppressFileSelection = true;
                    if (_currentFilePath is not null)
                    {
                        var files = FileList.ItemsSource as List<FileEntry>;
                        var idx = files?.FindIndex(f => f.FullPath == _currentFilePath) ?? -1;
                        FileList.SelectedIndex = idx;
                    }
                    _suppressFileSelection = false;
                    return;
                }
                if (answer == MessageBoxResult.Yes) SaveCurrentFile();
            }
        }

        OpenFile(entry.FullPath);
    }

    private void OpenFile(string path)
    {
        try
        {
            CodeEditor.Text = File.ReadAllText(path);
            _currentFilePath = path;
            _isDirty = false;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ExtensionToLanguage.TryGetValue(ext, out var lang) && lang != _language)
            {
                _language = lang;
                for (int i = 0; i < LanguagePicker.Items.Count; i++)
                {
                    if ((LanguagePicker.Items[i] as ComboBoxItem)?.Tag?.ToString() == lang)
                    {
                        LanguagePicker.SelectedIndex = i;
                        break;
                    }
                }
                RuntimeLabel.Text = RuntimeLabels.GetValueOrDefault(lang, "");
                ApplySyntaxHighlighting();
            }

            UpdateFileTitle();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to open: {ex.Message}";
        }
    }

    // ── Save ──────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath is not null)
        {
            SaveCurrentFile();
            return;
        }

        var env = EnvironmentContext.Current;
        if (env is null)
        {
            StatusText.Text = "Select an environment first.";
            return;
        }

        try
        {
            var ext = Extensions.GetValueOrDefault(_language, ".txt");
            var dlg = new InputDialog("Save File", $"File name (e.g. main{ext}):");
            var owner = Window.GetWindow(this);
            if (owner is not null) dlg.Owner = owner;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

            var fileName = dlg.ResultText.Trim();
            if (!Path.HasExtension(fileName)) fileName += ext;

            var filePath = Path.Combine(env.FolderPath, fileName);
            File.WriteAllText(filePath, CodeEditor.Text);
            _currentFilePath = filePath;
            _isDirty = false;
            UpdateFileTitle();
            RefreshFileList();
            StatusText.Text = $"Saved: {fileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void SaveCurrentFile()
    {
        if (_currentFilePath is null) return;
        try
        {
            File.WriteAllText(_currentFilePath, CodeEditor.Text);
            _isDirty = false;
            UpdateFileTitle();
            StatusText.Text = $"Saved: {Path.GetFileName(_currentFilePath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void UpdateFileTitle()
    {
        var name = _currentFilePath is not null ? Path.GetFileName(_currentFilePath) : "untitled";
        FileTitle.Text = _isDirty ? $"{name} •" : name;
    }

    // ── Language ──────────────────────────────────────────────────

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (LanguagePicker.SelectedItem is not ComboBoxItem item) return;
        var lang = item.Tag?.ToString() ?? "csharp";

        if (_language != lang && string.IsNullOrWhiteSpace(CodeEditor.Text)
            || CodeEditor.Text == Templates.GetValueOrDefault(_language))
        {
            CodeEditor.Text = Templates.GetValueOrDefault(lang, "");
        }

        _language = lang;
        RuntimeLabel.Text = RuntimeLabels.GetValueOrDefault(lang, "");
        ApplySyntaxHighlighting();
    }

    // ── Run ───────────────────────────────────────────────────────

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        var code = CodeEditor.Text;
        if (string.IsNullOrWhiteSpace(code)) return;

        string filePath;
        if (_currentFilePath is not null)
        {
            SaveCurrentFile();
            filePath = _currentFilePath;
        }
        else
        {
            var ext = Extensions.GetValueOrDefault(_language, ".txt");
            filePath = Path.Combine(_tempDir, $"script{ext}");
            await File.WriteAllTextAsync(filePath, code);
        }

        var (exe, args) = ResolveRuntime(_language, filePath);
        if (exe is null)
        {
            StatusText.Text = $"No runtime found for {_language}. Install one of the supported runtimes.";
            return;
        }

        var cmd = $"{exe} {args}";
        StatusText.Text = $"Launching: {cmd}";

        try
        {
            var workDir = _currentFilePath is not null
                ? Path.GetDirectoryName(_currentFilePath)!
                : _tempDir;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cmd} && pause || pause\"",
                UseShellExecute = true,
                WorkingDirectory = workDir
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
        }
    }

    private static (string? exe, string args) ResolveRuntime(string language, string filePath)
    {
        return language switch
        {
            "csharp" => FindFirst(["dotnet"], $"run \"{filePath}\""),
            "javascript" => FindFirstAlt(
                [("bun", $"run \"{filePath}\""), ("node", $"\"{filePath}\"")]),
            "python" => FindFirstAlt(
                [("uv", $"run \"{filePath}\""), ("python", $"\"{filePath}\""), ("python3", $"\"{filePath}\"")]),
            _ => (null, "")
        };
    }

    private static (string? exe, string args) FindFirst(string[] candidates, string args)
    {
        foreach (var c in candidates)
            if (ExistsOnPath(c))
                return (c, args);
        return (null, "");
    }

    private static (string? exe, string args) FindFirstAlt(
        (string exe, string args)[] candidates)
    {
        foreach (var (exe, args) in candidates)
            if (ExistsOnPath(exe))
                return (exe, args);
        return (null, "");
    }

    private static bool ExistsOnPath(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = exe,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // ── Editor helpers ─────────────────────────────────────────────

    private void OnCodeChanged(object? sender, EventArgs e)
    {
        if (!_isDirty && IsLoaded)
        {
            _isDirty = true;
            UpdateFileTitle();
        }
        if (IsLoaded) _autoSave.NotifyChanged();
    }

    private void OnCodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            OnRunClick(sender, e);
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnSaveClick(sender, e);
        }
    }

    // ── Ctrl+Wheel font zoom ─────────────────────────────────────

    private void OnEditorMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;

        var settings = AppSettings.Current;
        if (settings is null) return;

        var newSize = Math.Clamp(settings.EditorFontSize + (e.Delta > 0 ? 1 : -1), 10, 32);
        if (Math.Abs(newSize - settings.EditorFontSize) < 0.1) return;

        settings.EditorFontSize = newSize;
        Application.Current.Resources["EditorFontSize"] = newSize;
        settings.Save();
    }

    // ── File panel: New / Rename / Delete ─────────────────────────

    private void OnNewFileClick(object sender, RoutedEventArgs e)
    {
        var env = EnvironmentContext.Current;
        if (env is null || !Directory.Exists(env.FolderPath))
        {
            StatusText.Text = "Select an environment first.";
            return;
        }

        var ext = Extensions.GetValueOrDefault(_language, ".txt");
        var dlg = new InputDialog("New File", $"File name (e.g. main{ext}):");
        var owner = Window.GetWindow(this);
        if (owner is not null) dlg.Owner = owner;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

        var fileName = dlg.ResultText.Trim();
        if (!Path.HasExtension(fileName)) fileName += ext;

        var filePath = Path.Combine(env.FolderPath, fileName);
        if (File.Exists(filePath))
        {
            MessageBox.Show($"'{fileName}' already exists.", "File Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var template = "";
        var fileExt = Path.GetExtension(fileName).ToLowerInvariant();
        if (ExtensionToLanguage.TryGetValue(fileExt, out var lang))
            template = Templates.GetValueOrDefault(lang, "");

        File.WriteAllText(filePath, template);
        RefreshFileList();
        OpenFile(filePath);
        StatusText.Text = $"Created: {fileName}";
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
            MessageBox.Show($"'{newName}' already exists.", "File Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        File.Move(entry.FullPath, newPath);

        if (_currentFilePath is not null &&
            _currentFilePath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFilePath = newPath;
            UpdateFileTitle();

            var ext = Path.GetExtension(newPath).ToLowerInvariant();
            if (ExtensionToLanguage.TryGetValue(ext, out var lang) && lang != _language)
            {
                _language = lang;
                for (int i = 0; i < LanguagePicker.Items.Count; i++)
                {
                    if ((LanguagePicker.Items[i] as ComboBoxItem)?.Tag?.ToString() == lang)
                    {
                        LanguagePicker.SelectedIndex = i;
                        break;
                    }
                }
                RuntimeLabel.Text = RuntimeLabels.GetValueOrDefault(lang, "");
                ApplySyntaxHighlighting();
            }
        }

        RefreshFileList();
        StatusText.Text = $"Renamed to: {newName}";
    }

    private void OnDeleteFileClick(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry entry) return;

        var result = MessageBox.Show($"Delete '{entry.Name}'?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        File.Delete(entry.FullPath);

        if (_currentFilePath is not null &&
            _currentFilePath.Equals(entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFilePath = null;
            CodeEditor.Text = Templates.GetValueOrDefault(_language, "");
            _isDirty = false;
            UpdateFileTitle();
        }

        RefreshFileList();
        StatusText.Text = $"Deleted: {entry.Name}";
    }

    // ── IStatefulTool ──────────────────────────────────────────────

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["language"] = _language,
            ["code"] = CodeEditor.Text
        };

        if (_currentFilePath is not null)
            state["filePath"] = _currentFilePath;

        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("language", out var lang))
        {
            _language = lang;
            for (int i = 0; i < LanguagePicker.Items.Count; i++)
            {
                if ((LanguagePicker.Items[i] as ComboBoxItem)?.Tag?.ToString() == lang)
                {
                    LanguagePicker.SelectedIndex = i;
                    break;
                }
            }
            RuntimeLabel.Text = RuntimeLabels.GetValueOrDefault(lang, "");
            ApplySyntaxHighlighting();
        }

        if (state.TryGetValue("filePath", out var fp) && File.Exists(fp))
        {
            OpenFile(fp);
        }
        else if (state.TryGetValue("code", out var code))
        {
            CodeEditor.Text = code;
        }

        _isDirty = false;
        UpdateFileTitle();
        SyncFilePanel();
    }
}

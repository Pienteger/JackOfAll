using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JackOfAll.Lib;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public record FileEntry(string Name, string FullPath);

public partial class CodeRunnerToolView : UserControl, IStatefulTool
{
    private string _language = "csharp";
    private readonly string _tempDir;
    private List<NotepadWorkspace> _workspaces;
    private NotepadWorkspace? _activeWorkspace;
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
        _tempDir = Path.Combine(AppPaths.Root, "code-runner");
        Directory.CreateDirectory(_tempDir);
        _workspaces = CodeRunnerWorkspaceStore.Load();
        RefreshWorkspaceCombo();
        CodeEditor.Text = Templates["csharp"];
        UpdateLineNumbers();
    }

    // ── Workspace ─────────────────────────────────────────────────

    private void RefreshWorkspaceCombo()
    {
        WorkspaceCombo.Items.Clear();
        WorkspaceCombo.Items.Add(new ComboBoxItem { Content = "(No workspace)", Tag = "" });
        foreach (var ws in _workspaces)
            WorkspaceCombo.Items.Add(new ComboBoxItem { Content = ws.Name, Tag = ws.FolderPath });
        WorkspaceCombo.SelectedIndex = 0;
    }

    private void OnWorkspaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (WorkspaceCombo.SelectedItem is not ComboBoxItem item) return;

        var path = item.Tag?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            _activeWorkspace = null;
            ShowFilePanel(false);
            BtnRemoveWorkspace.Visibility = Visibility.Collapsed;
            return;
        }

        _activeWorkspace = _workspaces.FirstOrDefault(w => w.FolderPath == path);
        BtnRemoveWorkspace.Visibility = Visibility.Visible;
        ShowFilePanel(true);
        RefreshFileList();
    }

    private void OnAddWorkspace(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new InputDialog("New Workspace", "Workspace name:");
            var owner = Window.GetWindow(this);
            if (owner is not null) dlg.Owner = owner;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

            var name = dlg.ResultText.Trim();
            if (_workspaces.Any(w => w.Name == name))
            {
                MessageBox.Show($"Workspace '{name}' already exists.", "Duplicate",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderDlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select workspace folder" };
            if (folderDlg.ShowDialog() != true) return;

            var ws = new NotepadWorkspace(name, folderDlg.FolderName);
            _workspaces.Add(ws);
            CodeRunnerWorkspaceStore.Save(_workspaces);

            RefreshWorkspaceCombo();
            for (int i = 0; i < WorkspaceCombo.Items.Count; i++)
            {
                if ((WorkspaceCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == ws.FolderPath)
                {
                    WorkspaceCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add workspace: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRemoveWorkspace(object sender, RoutedEventArgs e)
    {
        if (_activeWorkspace is null) return;

        var result = MessageBox.Show($"Remove workspace '{_activeWorkspace.Name}'?\nFiles on disk will not be deleted.",
            "Remove Workspace", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _workspaces.RemoveAll(w => w.Name == _activeWorkspace.Name);
        CodeRunnerWorkspaceStore.Save(_workspaces);
        _activeWorkspace = null;
        _currentFilePath = null;
        UpdateFileTitle();
        RefreshWorkspaceCombo();
    }

    // ── File panel ────────────────────────────────────────────────

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
        if (_activeWorkspace is null || !Directory.Exists(_activeWorkspace.FolderPath))
        {
            FileList.ItemsSource = null;
            return;
        }

        var files = Directory.EnumerateFiles(_activeWorkspace.FolderPath)
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

        if (_activeWorkspace is null)
        {
            StatusText.Text = "Select a workspace first, or add one.";
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

            var filePath = Path.Combine(_activeWorkspace.FolderPath, fileName);
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

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isDirty && IsLoaded)
        {
            _isDirty = true;
            UpdateFileTitle();
        }
        UpdateLineNumbers();
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
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            var caretIndex = CodeEditor.CaretIndex;
            CodeEditor.Text = CodeEditor.Text.Insert(caretIndex, "    ");
            CodeEditor.CaretIndex = caretIndex + 4;
        }
    }

    private void UpdateLineNumbers()
    {
        if (LineNumbers is null) return;
        var count = CodeEditor.Text.Split('\n').Length;
        LineNumbers.Text = string.Join("\n", Enumerable.Range(1, Math.Max(count, 1)));
    }

    private void OnEditorScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0) return;
        var sv = GetScrollViewer(CodeEditor);
        if (sv is null) return;

        var lineNumberParent = LineNumbers.Parent as FrameworkElement;
        if (lineNumberParent?.Parent is Border border)
        {
            border.Margin = new Thickness(0, -sv.VerticalOffset, 0, 0);
            border.Padding = new Thickness(8, 6 + sv.VerticalOffset, 8, 6);
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject dep)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            if (child is ScrollViewer sv) return sv;
            var result = GetScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
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

        if (_activeWorkspace is not null)
            state["workspace"] = _activeWorkspace.Name;

        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("workspace", out var wsName))
        {
            var ws = _workspaces.FirstOrDefault(w => w.Name == wsName);
            if (ws is not null)
            {
                for (int i = 0; i < WorkspaceCombo.Items.Count; i++)
                {
                    if ((WorkspaceCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == ws.FolderPath)
                    {
                        WorkspaceCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

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
    }
}

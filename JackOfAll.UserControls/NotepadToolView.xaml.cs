using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class NotepadToolView : UserControl, IStatefulTool
{
    private string? _currentPath;
    private bool _isDirty;
    private List<NotepadWorkspace> _workspaces = [];
    private NotepadWorkspace? _activeWorkspace;
    private bool _suppressWorkspaceChange;

    public NotepadToolView()
    {
        InitializeComponent();
        LoadWorkspaces();
    }

    private void LoadWorkspaces()
    {
        _workspaces = WorkspaceStore.Load();
        RefreshWorkspaceCombo();
    }

    private void RefreshWorkspaceCombo()
    {
        _suppressWorkspaceChange = true;
        WorkspaceCombo.Items.Clear();
        WorkspaceCombo.Items.Add("(No workspace)");
        foreach (var ws in _workspaces)
            WorkspaceCombo.Items.Add(ws.Name);

        if (_activeWorkspace is not null)
        {
            var idx = _workspaces.FindIndex(w => w.Name == _activeWorkspace.Name);
            WorkspaceCombo.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else
        {
            WorkspaceCombo.SelectedIndex = 0;
        }

        _suppressWorkspaceChange = false;
    }

    private void OnWorkspaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWorkspaceChange) return;

        var idx = WorkspaceCombo.SelectedIndex;
        if (idx <= 0)
        {
            _activeWorkspace = null;
            HideFilePanel();
            BtnRemoveWorkspace.Visibility = Visibility.Collapsed;
        }
        else
        {
            _activeWorkspace = _workspaces[idx - 1];
            ShowFilePanel();
            RefreshFileList();
            BtnRemoveWorkspace.Visibility = Visibility.Visible;
        }
    }

    private void OnAddWorkspace(object sender, RoutedEventArgs e)
    {
        var nameDlg = new InputDialog("New Workspace", "Workspace name:");
        nameDlg.Owner = Window.GetWindow(this);
        if (nameDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDlg.ResultText))
            return;

        var name = nameDlg.ResultText.Trim();
        if (_workspaces.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A workspace with that name already exists.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderDlg = new OpenFolderDialog
        {
            Title = "Select workspace folder"
        };

        if (folderDlg.ShowDialog() != true)
            return;

        var ws = new NotepadWorkspace(name, folderDlg.FolderName);
        _workspaces.Add(ws);
        WorkspaceStore.Save(_workspaces);

        _activeWorkspace = ws;
        RefreshWorkspaceCombo();
        ShowFilePanel();
        RefreshFileList();
        BtnRemoveWorkspace.Visibility = Visibility.Visible;
    }

    private void OnRemoveWorkspace(object sender, RoutedEventArgs e)
    {
        if (_activeWorkspace is null) return;

        var result = MessageBox.Show(
            $"Remove workspace \"{_activeWorkspace.Name}\"?\nThis won't delete any files.",
            "Remove Workspace", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _workspaces.RemoveAll(w => w.Name == _activeWorkspace.Name);
        WorkspaceStore.Save(_workspaces);
        _activeWorkspace = null;
        RefreshWorkspaceCombo();
        HideFilePanel();
        BtnRemoveWorkspace.Visibility = Visibility.Collapsed;
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
        if (_activeWorkspace is null) return;

        var folder = _activeWorkspace.FolderPath;
        if (!Directory.Exists(folder))
        {
            FileList.ItemsSource = null;
            FilePanelEmpty.Text = "Folder not found";
            FilePanelEmpty.Visibility = Visibility.Visible;
            return;
        }

        var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".md" or ".markdown" or ".txt" or ".json" or ".xml" or ".yaml" or ".yml" or ".csv" or ".log" or ".ini" or ".cfg" or ".conf" or ".toml";
            })
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

    private bool _suppressFileSelection;

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
        if (PreviewToggle.IsChecked == true)
            UpdatePreview();
    }

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
        FileTitle.Text = "Untitled.md";
        UpdateStatus();

        _suppressFileSelection = true;
        FileList.SelectedItem = null;
        _suppressFileSelection = false;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var initialDir = _activeWorkspace?.FolderPath;

        var dlg = new OpenFileDialog
        {
            Filter = "Markdown files|*.md;*.markdown|Text files|*.txt|All files|*.*",
            DefaultExt = ".md"
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
        string defaultDir;
        if (_activeWorkspace is not null && Directory.Exists(_activeWorkspace.FolderPath))
        {
            defaultDir = _activeWorkspace.FolderPath;
        }
        else
        {
            defaultDir = Path.Combine(Lib.AppPaths.Root, "notes");
            Directory.CreateDirectory(defaultDir);
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Markdown files|*.md|Text files|*.txt|All files|*.*",
            DefaultExt = ".md",
            FileName = _currentPath is not null ? Path.GetFileName(_currentPath) : "Untitled.md",
            InitialDirectory = _currentPath is not null ? Path.GetDirectoryName(_currentPath)! : defaultDir
        };

        if (dlg.ShowDialog() == true)
        {
            _currentPath = dlg.FileName;
            File.WriteAllText(_currentPath, EditorBox.Text);
            _isDirty = false;
            FileTitle.Text = Path.GetFileName(_currentPath);
            UpdateStatus();
            RefreshFileList();
        }
    }

    private void OnPreviewToggle(object sender, RoutedEventArgs e)
    {
        if (PreviewToggle.IsChecked == true)
        {
            SplitterColumn.Width = new GridLength(6);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            Splitter.Visibility = Visibility.Visible;
            PreviewBrowser.Visibility = Visibility.Visible;
            UpdatePreview();
        }
        else
        {
            SplitterColumn.Width = new GridLength(0);
            PreviewColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
            PreviewBrowser.Visibility = Visibility.Collapsed;
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

    private void UpdatePreview()
    {
        var html = MarkdownService.ToHtml(EditorBox.Text);
        PreviewBrowser.NavigateToString(HtmlTemplate.Replace("{{CONTENT}}", html));
    }

    private void UpdateStatus()
    {
        var lines = EditorBox.Text.Split('\n').Length;
        var chars = EditorBox.Text.Length;
        var dirty = _isDirty ? " (modified)" : "";
        var wsLabel = _activeWorkspace is not null ? $"  [{_activeWorkspace.Name}]" : "";
        StatusText.Text = $"Lines: {lines}  Chars: {chars}{dirty}{wsLabel}";
    }

    public Dictionary<string, string> SaveState()
    {
        var state = new Dictionary<string, string>
        {
            ["text"] = EditorBox.Text,
            ["title"] = FileTitle.Text,
            ["preview"] = (PreviewToggle.IsChecked == true).ToString()
        };
        if (_currentPath is not null)
            state["path"] = _currentPath;
        if (_activeWorkspace is not null)
            state["workspace"] = _activeWorkspace.Name;
        return state;
    }

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("text", out var text))
            EditorBox.Text = text;
        if (state.TryGetValue("path", out var path))
            _currentPath = path;
        if (state.TryGetValue("title", out var title))
            FileTitle.Text = title;
        if (state.TryGetValue("preview", out var preview) && preview == "True")
            PreviewToggle.IsChecked = true;
        if (state.TryGetValue("workspace", out var wsName))
        {
            var ws = _workspaces.FirstOrDefault(w => w.Name == wsName);
            if (ws is not null)
            {
                _activeWorkspace = ws;
                RefreshWorkspaceCombo();
                ShowFilePanel();
                RefreshFileList();
                BtnRemoveWorkspace.Visibility = Visibility.Visible;
            }
        }
        _isDirty = false;
        UpdateStatus();
    }

    private record FileEntry(string Name, string FullPath);
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class NotepadToolView : UserControl
{
    private string? _currentPath;
    private bool _isDirty;

    public NotepadToolView()
    {
        InitializeComponent();
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
        EditorBox.Text = string.Empty;
        _currentPath = null;
        _isDirty = false;
        FileTitle.Text = "Untitled.md";
        UpdateStatus();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown files|*.md;*.markdown|Text files|*.txt|All files|*.*",
            DefaultExt = ".md"
        };

        if (dlg.ShowDialog() == true)
        {
            _currentPath = dlg.FileName;
            EditorBox.Text = File.ReadAllText(_currentPath);
            _isDirty = false;
            FileTitle.Text = Path.GetFileName(_currentPath);
            UpdateStatus();
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
        var dlg = new SaveFileDialog
        {
            Filter = "Markdown files|*.md|Text files|*.txt|All files|*.*",
            DefaultExt = ".md",
            FileName = _currentPath is not null ? Path.GetFileName(_currentPath) : "Untitled.md"
        };

        if (dlg.ShowDialog() == true)
        {
            _currentPath = dlg.FileName;
            File.WriteAllText(_currentPath, EditorBox.Text);
            _isDirty = false;
            FileTitle.Text = Path.GetFileName(_currentPath);
            UpdateStatus();
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
        StatusText.Text = $"Lines: {lines}  Chars: {chars}{dirty}";
    }
}

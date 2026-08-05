using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class DocumentVaultToolView : UserControl, IStatefulTool
{
    private static readonly string StorePath = Path.Combine(AppPaths.Root, "doc-vault.json");

    private static readonly string[] Categories =
        ["All", "VPN Config", "Database", "Certificate", "App Config", "SSH Config", "API Key", "License", "Other"];

    private readonly ObservableCollection<VaultDoc> _docs = [];
    private readonly ObservableCollection<VaultDoc> _filtered = [];
    private bool _suppressSave;
    private VaultDoc? _selected;

    public DocumentVaultToolView()
    {
        InitializeComponent();
        DocList.ItemsSource = _filtered;

        CategoryFilter.ItemsSource = Categories;
        CategoryFilter.SelectedIndex = 0;

        DocCategory.ItemsSource = Categories.Skip(1).ToArray();

        LoadFromDisk();
        RefreshFiltered();

        SearchBox.GotFocus += (_, _) => SearchPlaceholder.Visibility = Visibility.Collapsed;
        SearchBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                SearchPlaceholder.Visibility = Visibility.Visible;
        };
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var doc = new VaultDoc
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Untitled",
            Category = "Other",
            Content = "",
            Tags = "",
            Notes = "",
            AddedAt = DateTime.Now,
            ModifiedAt = DateTime.Now
        };

        _docs.Insert(0, doc);
        RefreshFiltered();
        SaveToDisk();

        DocList.SelectedItem = doc;
    }

    private void OnDocSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DocList.SelectedItem is not VaultDoc doc)
        {
            _selected = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        _selected = doc;
        _suppressSave = true;

        DocName.Text = doc.Name;
        DocCategory.SelectedItem = doc.Category;
        DocTags.Text = doc.Tags;
        DocNotes.Text = doc.Notes;
        DocContent.Text = doc.Content;

        _suppressSave = false;

        DetailPanel.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        UpdateDetailStatus();
    }

    private void OnFieldChanged(object sender, object e)
    {
        if (_suppressSave || _selected == null) return;

        _selected.Name = DocName.Text.Trim();
        _selected.Category = DocCategory.SelectedItem as string ?? "Other";
        _selected.Tags = DocTags.Text.Trim();
        _selected.Notes = DocNotes.Text.Trim();
        _selected.Content = DocContent.Text;
        _selected.ModifiedAt = DateTime.Now;

        RefreshFiltered(preserveSelection: true);
        SaveToDisk();
        UpdateDetailStatus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshFiltered();
    }

    private void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshFiltered();
    }

    private void RefreshFiltered(bool preserveSelection = false)
    {
        var prev = preserveSelection ? _selected : null;
        var query = SearchBox?.Text?.Trim() ?? "";
        var cat = CategoryFilter?.SelectedItem as string ?? "All";

        _filtered.Clear();

        foreach (var doc in _docs)
        {
            if (cat != "All" && doc.Category != cat) continue;

            if (!string.IsNullOrEmpty(query))
            {
                var match = doc.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || doc.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || doc.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || doc.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
            }

            _filtered.Add(doc);
        }

        CountText.Text = $"{_filtered.Count} of {_docs.Count} documents";

        if (prev != null && _filtered.Contains(prev))
            DocList.SelectedItem = prev;
    }

    private void OnCopyContent(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        try
        {
            Clipboard.SetDataObject(_selected.Content, true);
            DetailStatus.Text = "Content copied to clipboard.";
        }
        catch { DetailStatus.Text = "Failed to access clipboard."; }
    }

    private void OnExportFile(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var ext = GuessExtension(_selected.Category, _selected.Name);
        var dlg = new SaveFileDialog
        {
            FileName = SanitizeFileName(_selected.Name) + ext,
            Filter = "All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, _selected.Content);
            DetailStatus.Text = $"Exported to {Path.GetFileName(dlg.FileName)}";
        }
    }

    private void OnDeleteDoc(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var result = MessageBox.Show(
            $"Delete \"{_selected.Name}\"?",
            "Doc Vault", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _docs.Remove(_selected);
        _selected = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        RefreshFiltered();
        SaveToDisk();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var file in files)
            ImportFile(file);
    }

    private void ImportFile(string filePath)
    {
        try
        {
            var name = Path.GetFileName(filePath);
            var content = File.ReadAllText(filePath);
            var category = GuessCategory(name, content);

            var doc = new VaultDoc
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Category = category,
                Content = content,
                Tags = Path.GetExtension(filePath).TrimStart('.'),
                Notes = $"Imported from {filePath}",
                AddedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };

            _docs.Insert(0, doc);
            RefreshFiltered();
            SaveToDisk();
            DocList.SelectedItem = doc;
        }
        catch (Exception ex)
        {
            DetailStatus.Text = $"Import failed: {ex.Message}";
        }
    }

    private static string GuessCategory(string fileName, string content)
    {
        var lower = fileName.ToLowerInvariant();
        var contentLower = content.Length > 500 ? content[..500].ToLowerInvariant() : content.ToLowerInvariant();

        if (lower.EndsWith(".ovpn") || lower.Contains("vpn") || contentLower.Contains("remote ") && contentLower.Contains("proto "))
            return "VPN Config";
        if (lower.EndsWith(".pem") || lower.EndsWith(".crt") || lower.EndsWith(".cer") || lower.EndsWith(".pfx") || lower.EndsWith(".key")
            || contentLower.Contains("-----begin certificate"))
            return "Certificate";
        if (lower.Contains("connection") || contentLower.Contains("server=") || contentLower.Contains("data source=")
            || contentLower.Contains("jdbc:") || contentLower.Contains("mongodb://") || contentLower.Contains("postgres://"))
            return "Database";
        if (lower.Contains("ssh") || lower.EndsWith("_config") || contentLower.Contains("host ") && contentLower.Contains("identityfile"))
            return "SSH Config";
        if (lower.EndsWith(".env") || lower.Contains("appsettings") || lower.Contains("config.")
            || lower.EndsWith(".yml") || lower.EndsWith(".yaml") || lower.EndsWith(".toml"))
            return "App Config";
        if (lower.Contains("apikey") || lower.Contains("token") || lower.Contains("secret"))
            return "API Key";
        if (lower.Contains("license") || contentLower.Contains("mit license") || contentLower.Contains("apache license"))
            return "License";

        return "Other";
    }

    private static string GuessExtension(string category, string name)
    {
        if (Path.HasExtension(name)) return Path.GetExtension(name);

        return category switch
        {
            "VPN Config" => ".ovpn",
            "Certificate" => ".pem",
            "Database" => ".txt",
            "SSH Config" => ".conf",
            "App Config" => ".conf",
            "API Key" => ".txt",
            "License" => ".txt",
            _ => ".txt"
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private void UpdateDetailStatus()
    {
        if (_selected == null) return;
        var size = _selected.Content.Length < 1024
            ? $"{_selected.Content.Length} chars"
            : $"{_selected.Content.Length / 1024.0:F1} KB";
        var lines = _selected.Content.Split('\n').Length;
        DetailStatus.Text = $"{size} · {lines} lines · Modified {_selected.ModifiedAt:MMM dd, HH:mm}";
    }

    private void SaveToDisk()
    {
        try
        {
            AppPaths.EnsureRoot();
            var data = _docs.Select(d => new DocDto(d.Id, d.Name, d.Category, d.Content,
                d.Tags, d.Notes, d.AddedAt, d.ModifiedAt)).ToList();
            File.WriteAllText(StorePath, JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var json = File.ReadAllText(StorePath);
            var data = JsonSerializer.Deserialize<List<DocDto>>(json);
            if (data == null) return;

            foreach (var d in data)
            {
                _docs.Add(new VaultDoc
                {
                    Id = d.Id,
                    Name = d.Name,
                    Category = d.Category,
                    Content = d.Content,
                    Tags = d.Tags,
                    Notes = d.Notes,
                    AddedAt = d.AddedAt,
                    ModifiedAt = d.ModifiedAt
                });
            }
        }
        catch { }
    }

    public Dictionary<string, string> SaveState() => [];
    public void LoadState(Dictionary<string, string> state) { }

    private record DocDto(string Id, string Name, string Category, string Content,
        string Tags, string Notes, DateTime AddedAt, DateTime ModifiedAt);
}

public class VaultDoc
{
    private static readonly Dictionary<string, Brush> CategoryBrushes = new()
    {
        ["VPN Config"] = Frozen(0x42, 0x7B, 0xE5),
        ["Database"] = Frozen(0xE6, 0x7E, 0x22),
        ["Certificate"] = Frozen(0x2E, 0xA0, 0x43),
        ["App Config"] = Frozen(0x7B, 0x1F, 0xA2),
        ["SSH Config"] = Frozen(0x00, 0x89, 0x7B),
        ["API Key"] = Frozen(0xE5, 0x39, 0x35),
        ["License"] = Frozen(0xF9, 0xA8, 0x25),
        ["Other"] = Frozen(0x75, 0x75, 0x75),
    };

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Content { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public Brush CategoryBrush => CategoryBrushes.GetValueOrDefault(Category, CategoryBrushes["Other"]);

    public string CategoryShort => Category switch
    {
        "VPN Config" => "VPN",
        "App Config" => "CFG",
        "SSH Config" => "SSH",
        "API Key" => "KEY",
        "Certificate" => "CERT",
        "Database" => "DB",
        "License" => "LIC",
        _ => "OTHER"
    };

    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content)) return "(empty)";
            var text = Content.Length > 120 ? Content[..120] : Content;
            return text.ReplaceLineEndings(" ").Trim();
        }
    }

    public string DateText => AddedAt.Date == DateTime.Today
        ? $"Today {AddedAt:HH:mm}"
        : AddedAt.ToString("MMM dd, yyyy");

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

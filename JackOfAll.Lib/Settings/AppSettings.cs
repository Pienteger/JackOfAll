using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JackOfAll.Lib.Settings;

public enum ThemeMode
{
    Dark,
    Light,
    System
}

public enum AutoSaveMode
{
    Off,
    AfterDelay,
    OnFocusChange
}

public partial class AppSettings : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.Root, "settings.json");

    public static AppSettings? Current { get; private set; }

    [ObservableProperty]
    private double _fontSize = 13;

    [ObservableProperty]
    private double _editorFontSize = 14;

    [ObservableProperty]
    private ThemeMode _themeMode = ThemeMode.Dark;

    [ObservableProperty]
    private AutoSaveMode _autoSaveMode = AutoSaveMode.AfterDelay;

    [ObservableProperty]
    private int _autoSaveDelayMs = 1000;

    [ObservableProperty]
    private string _gitHubToken = "";
    [ObservableProperty]
    private int _apiResponsePreviewLimitMb = 10;

    [ObservableProperty]
    private int _apiHistoryEntriesPerRequest = 50;

    [ObservableProperty]
    private int _apiHistoryStorageLimitMb = 100;

    [ObservableProperty]
    private int _clipboardMaxEntries = 100;

    [ObservableProperty]
    private int _clipboardMaxEntryKb = 256;

    [ObservableProperty]
    private int _clipboardStorageLimitMb = 5;

    [ObservableProperty]
    private int _clipboardImageStorageMb = 300;

    [ObservableProperty]
    private bool _clipboardRecordInBackground = true;

    [ObservableProperty]
    private bool _clipboardQuickPicker = true;

    [ObservableProperty]
    private int _logViewerScanLimitMb = 256;

    [ObservableProperty]
    private int _logViewerRetainedEntries = 100000;

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        var dto = new SettingsDto
        {
            FontSize = FontSize,
            EditorFontSize = EditorFontSize,
            ThemeMode = ThemeMode.ToString(),
            AutoSaveMode = AutoSaveMode.ToString(),
            AutoSaveDelayMs = AutoSaveDelayMs,
            GitHubToken = GitHubToken,
            ApiResponsePreviewLimitMb = ApiResponsePreviewLimitMb,
            ApiHistoryEntriesPerRequest = ApiHistoryEntriesPerRequest,
            ApiHistoryStorageLimitMb = ApiHistoryStorageLimitMb,
            ClipboardMaxEntries = ClipboardMaxEntries,
            ClipboardMaxEntryKb = ClipboardMaxEntryKb,
            ClipboardStorageLimitMb = ClipboardStorageLimitMb,
            ClipboardImageStorageMb = ClipboardImageStorageMb,
            ClipboardRecordInBackground = ClipboardRecordInBackground,
            ClipboardQuickPicker = ClipboardQuickPicker,
            LogViewerScanLimitMb = LogViewerScanLimitMb,
            LogViewerRetainedEntries = LogViewerRetainedEntries
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        Current = settings;
        if (!File.Exists(SettingsPath)) return settings;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json);
            if (dto is null) return settings;

            settings.FontSize = Math.Clamp(dto.FontSize, 10, 24);
            settings.EditorFontSize = Math.Clamp(dto.EditorFontSize, 10, 32);
            if (Enum.TryParse<ThemeMode>(dto.ThemeMode, out var mode))
                settings.ThemeMode = mode;
            if (Enum.TryParse<AutoSaveMode>(dto.AutoSaveMode, out var autoSave))
                settings.AutoSaveMode = autoSave;
            settings.AutoSaveDelayMs = Math.Clamp(dto.AutoSaveDelayMs, 200, 30000);
            settings.GitHubToken = dto.GitHubToken;
            settings.ApiResponsePreviewLimitMb = Math.Clamp(dto.ApiResponsePreviewLimitMb, 1, 100);
            settings.ApiHistoryEntriesPerRequest = Math.Clamp(dto.ApiHistoryEntriesPerRequest, 10, 200);
            settings.ApiHistoryStorageLimitMb = Math.Clamp(dto.ApiHistoryStorageLimitMb, 10, 500);
            settings.ClipboardMaxEntries = Math.Clamp(dto.ClipboardMaxEntries, 10, 500);
            settings.ClipboardMaxEntryKb = Math.Clamp(dto.ClipboardMaxEntryKb, 16, 4096);
            settings.ClipboardStorageLimitMb = Math.Clamp(dto.ClipboardStorageLimitMb, 1, 100);
            settings.ClipboardImageStorageMb = Math.Clamp(dto.ClipboardImageStorageMb, 0, 2000);
            settings.ClipboardRecordInBackground = dto.ClipboardRecordInBackground;
            settings.ClipboardQuickPicker = dto.ClipboardQuickPicker;
            settings.LogViewerScanLimitMb = Math.Clamp(dto.LogViewerScanLimitMb, 16, 4096);
            settings.LogViewerRetainedEntries = Math.Clamp(dto.LogViewerRetainedEntries, 10000, 500000);
        }
        catch
        {
            // Corrupted file — use defaults
        }

        return settings;
    }

    private sealed class SettingsDto
    {
        public double FontSize { get; set; } = 13;
        public double EditorFontSize { get; set; } = 14;
        public string ThemeMode { get; set; } = "Dark";
        public string AutoSaveMode { get; set; } = "AfterDelay";
        public int AutoSaveDelayMs { get; set; } = 1000;
        public string GitHubToken { get; set; } = "";
        public int ApiResponsePreviewLimitMb { get; set; } = 10;
        public int ApiHistoryEntriesPerRequest { get; set; } = 50;
        public int ApiHistoryStorageLimitMb { get; set; } = 100;
        public int ClipboardMaxEntries { get; set; } = 100;
        public int ClipboardMaxEntryKb { get; set; } = 256;
        public int ClipboardStorageLimitMb { get; set; } = 5;
        public int ClipboardImageStorageMb { get; set; } = 300;
        public bool ClipboardRecordInBackground { get; set; } = true;
        public bool ClipboardQuickPicker { get; set; } = true;
        public int LogViewerScanLimitMb { get; set; } = 256;
        public int LogViewerRetainedEntries { get; set; } = 100000;
    }
}

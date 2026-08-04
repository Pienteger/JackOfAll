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
            ApiHistoryStorageLimitMb = ApiHistoryStorageLimitMb
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
    }
}

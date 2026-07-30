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
    private string _gitHubToken = "";

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        var dto = new SettingsDto { FontSize = FontSize, EditorFontSize = EditorFontSize, ThemeMode = ThemeMode.ToString(), GitHubToken = GitHubToken };
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
            settings.GitHubToken = dto.GitHubToken;
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
        public string GitHubToken { get; set; } = "";
    }
}

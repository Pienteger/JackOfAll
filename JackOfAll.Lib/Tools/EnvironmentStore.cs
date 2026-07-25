using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Tools;

public static class EnvironmentStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "environments.json");
    private static readonly string ActivePath = Path.Combine(AppPaths.Root, "active-environment.txt");

    public static List<AppEnvironment> Load()
    {
        if (!File.Exists(FilePath)) return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<AppEnvironment>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<AppEnvironment> environments)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(environments, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static string? LoadActiveName()
    {
        if (!File.Exists(ActivePath)) return null;
        try
        {
            var name = File.ReadAllText(ActivePath).Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch { return null; }
    }

    public static void SaveActiveName(string? name)
    {
        var dir = Path.GetDirectoryName(ActivePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ActivePath, name ?? "");
    }
}

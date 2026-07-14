using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Tools;

public static class ArchiveStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackOfAll", "archives.json");

    public static List<ArchivedTab> Load()
    {
        if (!File.Exists(FilePath)) return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ArchivedTab>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<ArchivedTab> archives)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(archives, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

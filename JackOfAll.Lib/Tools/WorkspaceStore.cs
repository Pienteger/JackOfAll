using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Tools;

public static class WorkspaceStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "workspaces.json");

    public static List<NotepadWorkspace> Load()
    {
        if (!File.Exists(FilePath)) return [];

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<NotepadWorkspace>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<NotepadWorkspace> workspaces)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(workspaces, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

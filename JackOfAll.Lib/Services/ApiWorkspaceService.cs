using System.IO;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public static class ApiWorkspaceService
{
    private static readonly string WorkspacesRoot = Path.Combine(AppPaths.Root, "api-workspaces");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<string> List()
    {
        if (!Directory.Exists(WorkspacesRoot)) return [];
        return Directory.GetDirectories(WorkspacesRoot)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Create(string name)
    {
        var dir = Path.Combine(WorkspacesRoot, name);
        Directory.CreateDirectory(dir);
        var reqPath = Path.Combine(dir, "requests.json");
        if (!File.Exists(reqPath))
            File.WriteAllText(reqPath, "[]");
    }

    public static void Delete(string name)
    {
        var dir = Path.Combine(WorkspacesRoot, name);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }

    public static List<ApiRequest> LoadRequests(string workspace)
    {
        var path = Path.Combine(WorkspacesRoot, workspace, "requests.json");
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ApiRequest>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void SaveRequests(string workspace, List<ApiRequest> requests)
    {
        var dir = Path.Combine(WorkspacesRoot, workspace);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(requests, JsonOpts);
        File.WriteAllText(Path.Combine(dir, "requests.json"), json);
    }
}

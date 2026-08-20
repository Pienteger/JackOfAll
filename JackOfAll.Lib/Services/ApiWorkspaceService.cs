using System.IO;
using System.Text.Json;
using JackOfAll.Lib.Tools;

namespace JackOfAll.Lib.Services;

public static class ApiWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void Create(AppEnvironment environment)
    {
        var dir = ToolWorkspacePaths.GetApiClientFolder(environment);
        var reqPath = Path.Combine(dir, "requests.json");
        if (!File.Exists(reqPath))
            File.WriteAllText(reqPath, "[]");
    }

    public static List<ApiRequest> LoadRequests(AppEnvironment environment)
    {
        var path = Path.Combine(ToolWorkspacePaths.GetApiClientFolder(environment), "requests.json");
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ApiRequest>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void SaveRequests(AppEnvironment environment, List<ApiRequest> requests)
    {
        var dir = ToolWorkspacePaths.GetApiClientFolder(environment);
        var json = JsonSerializer.Serialize(requests, JsonOpts);
        File.WriteAllText(Path.Combine(dir, "requests.json"), json);
    }
}
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public static class ApiEnvironmentService
{
    private static readonly string EnvDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "JackOfAll", "environments");

    private static string _activeEnv = "";
    private static Dictionary<string, string> _variables = new();

    public static string ActiveEnvironment => _activeEnv;
    public static IReadOnlyDictionary<string, string> Variables => _variables;

    public static List<string> ListEnvironments()
    {
        if (!Directory.Exists(EnvDir)) return [];
        return Directory.GetFiles(EnvDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
    }

    public static void SetActive(string name)
    {
        _activeEnv = name;
        _variables = Load(name);
    }

    public static void ClearActive()
    {
        _activeEnv = "";
        _variables = new();
    }

    public static Dictionary<string, string> Load(string name)
    {
        var path = Path.Combine(EnvDir, $"{name}.json");
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(string name, Dictionary<string, string> vars)
    {
        Directory.CreateDirectory(EnvDir);
        var path = Path.Combine(EnvDir, $"{name}.json");
        var json = JsonSerializer.Serialize(vars, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        if (name == _activeEnv)
            _variables = new Dictionary<string, string>(vars);
    }

    public static void CreateEnvironment(string name)
    {
        Save(name, new());
    }

    public static void DeleteEnvironment(string name)
    {
        var path = Path.Combine(EnvDir, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);
        if (name == _activeEnv) ClearActive();
    }

    public static void SetVariable(string key, string value)
    {
        if (string.IsNullOrEmpty(_activeEnv)) return;
        _variables[key] = value;
        Save(_activeEnv, _variables);
    }

    public static void RemoveVariable(string key)
    {
        if (string.IsNullOrEmpty(_activeEnv)) return;
        _variables.Remove(key);
        Save(_activeEnv, _variables);
    }

    public static string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input) || _variables.Count == 0) return input;
        return Regex.Replace(input, @"\{\{(\w+)\}\}", m =>
        {
            var varName = m.Groups[1].Value;
            return _variables.TryGetValue(varName, out var val) ? val : m.Value;
        });
    }

    public static string? ExtractJsonPath(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;

            var segments = path.TrimStart('$', '.').Split('.');
            foreach (var seg in segments)
            {
                if (string.IsNullOrEmpty(seg)) continue;

                if (seg.EndsWith(']') && seg.Contains('['))
                {
                    var bracket = seg.IndexOf('[');
                    var prop = seg[..bracket];
                    var idxStr = seg[(bracket + 1)..^1];

                    if (!string.IsNullOrEmpty(prop))
                    {
                        if (element.ValueKind != JsonValueKind.Object) return null;
                        if (!element.TryGetProperty(prop, out element)) return null;
                    }

                    if (int.TryParse(idxStr, out var idx))
                    {
                        if (element.ValueKind != JsonValueKind.Array) return null;
                        if (idx < 0 || idx >= element.GetArrayLength()) return null;
                        element = element[idx];
                    }
                    else return null;
                }
                else
                {
                    if (element.ValueKind != JsonValueKind.Object) return null;
                    if (!element.TryGetProperty(seg, out element)) return null;
                }
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => element.GetRawText()
            };
        }
        catch { return null; }
    }

    public static string? ExtractRegex(string text, string pattern)
    {
        try
        {
            var match = Regex.Match(text, pattern);
            if (!match.Success) return null;
            return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }
        catch { return null; }
    }
}

public record ExtractionRule
{
    public string Variable { get; set; } = "";
    public string Source { get; set; } = "jsonpath";
    public string Expression { get; set; } = "";
}

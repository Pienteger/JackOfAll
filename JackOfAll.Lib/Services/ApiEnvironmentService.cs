using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public static class ApiEnvironmentService
{
    private static readonly string EnvDir = Path.Combine(AppPaths.Root, "environments");
    private static readonly Regex VariablePattern = new(@"\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex ValidVariableNamePattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private static readonly Regex ValidEnvironmentNamePattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private static string _activeEnv = "";
    private static Dictionary<string, string> _variables = new();

    public static string ActiveEnvironment => _activeEnv;
    public static IReadOnlyDictionary<string, string> Variables => _variables;

    public static List<string> ListEnvironments()
    {
        if (!Directory.Exists(EnvDir)) return [];
        return Directory.GetFiles(EnvDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n) && IsValidEnvironmentName(n!))
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
    }

    public static string NormalizeEnvironmentName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant().Replace(' ', '-');
        normalized = Regex.Replace(normalized, @"[^a-z0-9_.-]+", "-");
        normalized = Regex.Replace(normalized, @"-+", "-").Trim('-', '.');
        return IsValidEnvironmentName(normalized) ? normalized : "";
    }

    public static bool IsValidEnvironmentName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && ValidEnvironmentNamePattern.IsMatch(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name is not "." and not "..";

    public static bool IsValidVariableName(string key) =>
        !string.IsNullOrWhiteSpace(key) && ValidVariableNamePattern.IsMatch(key.Trim());

    public static void SetActive(string name)
    {
        if (!IsValidEnvironmentName(name))
        {
            ClearActive();
            return;
        }

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
        if (!IsValidEnvironmentName(name)) return new();
        var path = GetEnvironmentPath(name);
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)?
                .Where(kv => IsValidVariableName(kv.Key))
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? "") ?? new();
        }
        catch { return new(); }
    }

    public static void Save(string name, Dictionary<string, string> vars)
    {
        if (!IsValidEnvironmentName(name)) return;

        Directory.CreateDirectory(EnvDir);
        var path = GetEnvironmentPath(name);
        var clean = vars
            .Where(kv => IsValidVariableName(kv.Key))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? "");
        var json = JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        if (name == _activeEnv)
            _variables = new Dictionary<string, string>(clean);
    }

    public static void CreateEnvironment(string name)
    {
        if (!IsValidEnvironmentName(name)) return;
        if (File.Exists(GetEnvironmentPath(name))) return;
        Save(name, new());
    }

    public static void DeleteEnvironment(string name)
    {
        if (!IsValidEnvironmentName(name)) return;
        var path = GetEnvironmentPath(name);
        if (File.Exists(path)) File.Delete(path);
        if (name == _activeEnv) ClearActive();
    }

    public static void SetVariable(string key, string value)
    {
        if (string.IsNullOrEmpty(_activeEnv)) return;
        key = key.Trim();
        if (!IsValidVariableName(key)) return;
        _variables[key] = value;
        Save(_activeEnv, _variables);
    }

    public static void RemoveVariable(string key)
    {
        if (string.IsNullOrEmpty(_activeEnv)) return;
        key = key.Trim();
        if (!IsValidVariableName(key)) return;
        _variables.Remove(key);
        Save(_activeEnv, _variables);
    }

    public static string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input) || _variables.Count == 0) return input;
        return VariablePattern.Replace(input, m =>
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
            return ExtractJsonPath(doc.RootElement, path);
        }
        catch { return null; }
    }

    public static string? ExtractJsonPath(JsonElement root, string path)
    {
        try
        {
            var element = root;
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
        catch (InvalidOperationException) { return null; }
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

    private static string GetEnvironmentPath(string name) => Path.Combine(EnvDir, $"{name}.json");
}

public record ExtractionRule
{
    public string Variable { get; set; } = "";
    public string Source { get; set; } = "jsonpath";
    public string Expression { get; set; } = "";
}

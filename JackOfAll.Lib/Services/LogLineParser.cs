using System.Text.Json;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public sealed record LogLine(
    string Level,
    string Timestamp,
    string Message,
    string Source,
    long LineNumber,
    bool IsStructured = true);

public static partial class LogLineParser
{
    [GeneratedRegex(@"^\[?(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[^\]]*)\]?\s*\[?(?<lvl>TRACE|DEBUG|INFORMATION|INFO|WARNING|WARN|ERROR|FATAL|CRITICAL|VERBOSE|VRB|DBG|INF|WRN|ERR|FTL)\]?\s*(?<msg>.*)", RegexOptions.IgnoreCase)]
    private static partial Regex SerilogRegex();

    [GeneratedRegex(@"^(?<ts>\d{4}[-/]\d{2}[-/]\d{2}[T ]\d{2}:\d{2}:\d{2}[^ ]*)\s+(?<lvl>TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\s+(?<msg>.*)", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleRegex();

    public static LogLine Parse(string raw, long lineNumber)
    {
        var line = raw.TrimEnd('\r');
        if (TryParseJson(line, lineNumber) is { } json) return json;
        if (TryParsePattern(line, lineNumber) is { } pattern) return pattern;
        return new LogLine("INFO", "", line, "", lineNumber, false);
    }

    public static bool IsContinuation(string raw) =>
        raw.Length > 0 && (char.IsWhiteSpace(raw[0]) || raw.StartsWith("at ", StringComparison.Ordinal));

    public static string NormalizeLevel(string level) => level.ToUpperInvariant() switch
    {
        "VRB" or "VERBOSE" => "TRACE",
        "DBG" => "DEBUG",
        "INF" or "INFORMATION" => "INFO",
        "WRN" or "WARNING" => "WARN",
        "ERR" or "CRITICAL" => "ERROR",
        "FTL" => "FATAL",
        var value => value
    };

    private static LogLine? TryParseJson(string line, long lineNumber)
    {
        if (line.Length < 2 || line[0] != '{') return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var level = Get(root, "@l", "Level", "level", "severity", "log.level") ?? "INFO";
            var timestamp = Get(root, "@t", "Timestamp", "timestamp", "time", "ts", "@timestamp") ?? "";
            var message = Get(root, "@m", "Message", "message", "msg", "@mt") ?? line;
            var source = Get(root, "SourceContext", "source", "logger", "category", "Logger") ?? "";
            return new LogLine(NormalizeLevel(level), timestamp, message, source, lineNumber);
        }
        catch
        {
            return null;
        }
    }

    private static string? Get(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var property))
                return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        return null;
    }

    private static LogLine? TryParsePattern(string line, long lineNumber)
    {
        var match = SerilogRegex().Match(line);
        if (!match.Success) match = SimpleRegex().Match(line);
        if (!match.Success) return null;
        return new LogLine(
            NormalizeLevel(match.Groups["lvl"].Value),
            match.Groups["ts"].Value,
            match.Groups["msg"].Value.Trim(),
            "",
            lineNumber);
    }
}

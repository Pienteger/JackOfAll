using System.Text.Json;

namespace JackOfAll.Lib.Services;

public static class JsonService
{
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true
    };

    public static (string? Result, string? Error) Format(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            return (JsonSerializer.Serialize(doc.RootElement, PrettyOptions), null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON: {ex.Message}");
        }
    }

    public static (string? Result, string? Error) Minify(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            return (JsonSerializer.Serialize(doc.RootElement), null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON: {ex.Message}");
        }
    }

    public static string Escape(string input)
    {
        return JsonSerializer.Serialize(input);
    }

    public static (string? Result, string? Error) Unescape(string input)
    {
        try
        {
            var result = JsonSerializer.Deserialize<string>(input);
            return (result, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Not a valid JSON string: {ex.Message}");
        }
    }

    public static string? Validate(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            return null;
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }
}

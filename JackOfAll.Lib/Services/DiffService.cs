using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackOfAll.Lib.Services;

public enum DiffMode
{
    Text,
    JsonSemantic
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record DiffResult(
    bool Success,
    string Summary,
    List<DiffLine> Lines,
    string? Error = null);

public static class DiffService
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public static DiffResult Compare(string left, string right, DiffMode mode, bool ignoreWhitespace)
    {
        try
        {
            if (mode == DiffMode.JsonSemantic)
            {
                left = NormalizeJson(left);
                right = NormalizeJson(right);
                ignoreWhitespace = false;
            }

            var leftLines = SplitLines(left, ignoreWhitespace);
            var rightLines = SplitLines(right, ignoreWhitespace);
            var diffLines = BuildDiff(leftLines, rightLines);

            var added = diffLines.Count(l => l.Kind == DiffLineKind.Added);
            var removed = diffLines.Count(l => l.Kind == DiffLineKind.Removed);
            var summary = added == 0 && removed == 0
                ? "No differences found."
                : $"{added} added, {removed} removed.";

            return new DiffResult(true, summary, diffLines);
        }
        catch (JsonException ex)
        {
            return new DiffResult(false, "JSON compare failed.", [], ex.Message);
        }
        catch (Exception ex)
        {
            return new DiffResult(false, "Compare failed.", [], ex.Message);
        }
    }

    public static string ToUnifiedDiff(IEnumerable<DiffLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var prefix = line.Kind switch
            {
                DiffLineKind.Added => "+ ",
                DiffLineKind.Removed => "- ",
                _ => "  "
            };
            builder.Append(prefix).AppendLine(line.Text);
        }

        return builder.ToString();
    }

    private static string NormalizeJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new JsonException("Both sides must contain valid JSON.");

        var node = JsonNode.Parse(input) ?? throw new JsonException("JSON value cannot be empty.");
        var normalized = NormalizeNode(node) ?? throw new JsonException("JSON value cannot be empty.");
        return normalized.ToJsonString(PrettyJsonOptions);
    }

    private static JsonNode? NormalizeNode(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => NormalizeObject(obj),
            JsonArray arr => NormalizeArray(arr),
            JsonValue value => JsonNode.Parse(value.ToJsonString()),
            null => null,
            _ => JsonNode.Parse(node.ToJsonString())
        };
    }

    private static JsonObject NormalizeObject(JsonObject obj)
    {
        var normalized = new JsonObject();
        foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            normalized[property.Key] = NormalizeNode(property.Value);
        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray arr)
    {
        var normalized = new JsonArray();
        foreach (var item in arr)
            normalized.Add(NormalizeNode(item));
        return normalized;
    }

    private static List<string> SplitLines(string input, bool ignoreWhitespace)
    {
        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return ignoreWhitespace
            ? lines.Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToList()
            : lines;
    }

    private static List<DiffLine> BuildDiff(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var lengths = new int[left.Count + 1, right.Count + 1];
        for (var i = left.Count - 1; i >= 0; i--)
        {
            for (var j = right.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = left[i] == right[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        var li = 0;
        var ri = 0;

        while (li < left.Count && ri < right.Count)
        {
            if (left[li] == right[ri])
            {
                result.Add(new DiffLine(DiffLineKind.Context, left[li]));
                li++;
                ri++;
            }
            else if (lengths[li + 1, ri] >= lengths[li, ri + 1])
            {
                result.Add(new DiffLine(DiffLineKind.Removed, left[li]));
                li++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Added, right[ri]));
                ri++;
            }
        }

        while (li < left.Count)
            result.Add(new DiffLine(DiffLineKind.Removed, left[li++]));

        while (ri < right.Count)
            result.Add(new DiffLine(DiffLineKind.Added, right[ri++]));

        return result;
    }
}

using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace JackOfAll.Lib.Services;

public static class OpenApiImporter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete", "head", "options"];

    public static async Task<List<ApiRequest>> FromFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        return Parse(doc);
    }

    public static async Task<List<ApiRequest>> FromUrlAsync(string url)
    {
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        return Parse(doc);
    }

    public static List<ApiRequest> Parse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var baseUrl = GetBaseUrl(root);
        var securityScheme = GetSecurityScheme(root);
        var requests = new List<ApiRequest>();

        if (!root.TryGetProperty("paths", out var paths))
            return requests;

        foreach (var pathEntry in paths.EnumerateObject())
        {
            var pathTemplate = pathEntry.Name;

            foreach (var method in Methods)
            {
                if (!pathEntry.Value.TryGetProperty(method, out var operation))
                    continue;

                var req = new ApiRequest
                {
                    Method = method.ToUpperInvariant(),
                    Url = baseUrl + pathTemplate
                };

                req.Name = GetOperationName(operation, method, pathTemplate);

                ParseParameters(operation, pathEntry.Value, root, req);
                ParseRequestBody(operation, root, req);

                if (securityScheme is not null)
                {
                    req.AuthType = securityScheme.Value.type;
                    req.AuthValue = securityScheme.Value.placeholder;
                }

                requests.Add(req);
            }
        }

        return requests;
    }

    private static string GetBaseUrl(JsonElement root)
    {
        if (root.TryGetProperty("servers", out var servers)
            && servers.GetArrayLength() > 0)
        {
            var first = servers[0];
            if (first.TryGetProperty("url", out var url))
                return url.GetString()?.TrimEnd('/') ?? "";
        }
        return "";
    }

    private static (string type, string placeholder)? GetSecurityScheme(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components))
            return null;
        if (!components.TryGetProperty("securitySchemes", out var schemes))
            return null;

        foreach (var scheme in schemes.EnumerateObject())
        {
            if (!scheme.Value.TryGetProperty("type", out var typeProp))
                continue;

            var type = typeProp.GetString();
            if (type == "http" && scheme.Value.TryGetProperty("scheme", out var schemeProp))
            {
                var schemeStr = schemeProp.GetString()?.ToLowerInvariant();
                if (schemeStr == "bearer")
                    return ("Bearer", "{{token}}");
                if (schemeStr == "basic")
                    return ("Basic", "{{username}}:{{password}}");
            }
            if (type == "apiKey")
                return ("Bearer", "{{api_key}}");
        }
        return null;
    }

    private static string GetOperationName(JsonElement operation, string method, string path)
    {
        if (operation.TryGetProperty("operationId", out var opId))
        {
            var id = opId.GetString();
            if (!string.IsNullOrWhiteSpace(id)) return id;
        }
        if (operation.TryGetProperty("summary", out var summary))
        {
            var s = summary.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return $"{method.ToUpperInvariant()} {path}";
    }

    private static void ParseParameters(JsonElement operation, JsonElement pathItem, JsonElement root, ApiRequest req)
    {
        var parameters = new List<JsonElement>();

        if (pathItem.TryGetProperty("parameters", out var pathParams))
            foreach (var p in pathParams.EnumerateArray())
                parameters.Add(ResolveRef(p, root));

        if (operation.TryGetProperty("parameters", out var opParams))
            foreach (var p in opParams.EnumerateArray())
                parameters.Add(ResolveRef(p, root));

        foreach (var param in parameters)
        {
            if (!param.TryGetProperty("in", out var inProp)
                || !param.TryGetProperty("name", out var nameProp))
                continue;

            var location = inProp.GetString();
            var name = nameProp.GetString() ?? "";

            switch (location)
            {
                case "query":
                    req.Params.Add(new KeyValueItem { Enabled = true, Key = name, Value = "" });
                    break;
                case "header":
                    req.Headers.Add(new KeyValueItem { Enabled = true, Key = name, Value = "" });
                    break;
            }
        }
    }

    private static void ParseRequestBody(JsonElement operation, JsonElement root, ApiRequest req)
    {
        if (!operation.TryGetProperty("requestBody", out var body))
            return;
        body = ResolveRef(body, root);
        if (!body.TryGetProperty("content", out var content))
            return;

        if (content.TryGetProperty("application/json", out var jsonContent))
        {
            req.BodyType = "raw";
            req.Body = GenerateExampleFromContent(jsonContent, root);
            req.Headers.Add(new KeyValueItem
            {
                Enabled = true,
                Key = "Content-Type",
                Value = "application/json"
            });
        }
        else if (content.TryGetProperty("application/x-www-form-urlencoded", out var formContent))
        {
            req.BodyType = "x-www-form-urlencoded";
            ParseFormProperties(formContent, root, req.FormData);
        }
        else if (content.TryGetProperty("multipart/form-data", out var multipartContent))
        {
            req.BodyType = "form-data";
            ParseFormProperties(multipartContent, root, req.FormData);
        }
    }

    private static void ParseFormProperties(JsonElement mediaType, JsonElement root, List<KeyValueItem> formData)
    {
        if (!mediaType.TryGetProperty("schema", out var schema))
            return;
        schema = ResolveRef(schema, root);
        if (!schema.TryGetProperty("properties", out var props))
            return;

        foreach (var prop in props.EnumerateObject())
        {
            formData.Add(new KeyValueItem
            {
                Enabled = true,
                Key = prop.Name,
                Value = ""
            });
        }
    }

    private static string GenerateExampleFromContent(JsonElement mediaType, JsonElement root)
    {
        if (mediaType.TryGetProperty("example", out var example))
            return JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true });

        if (!mediaType.TryGetProperty("schema", out var schema))
            return "{}";

        var resolved = ResolveRef(schema, root);
        var generated = GenerateFromSchema(resolved, root, 0, []);
        return JsonSerializer.Serialize(generated, new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonElement ResolveRef(JsonElement element, JsonElement root)
    {
        if (element.TryGetProperty("$ref", out var refProp))
        {
            var refPath = refProp.GetString();
            if (refPath is not null && refPath.StartsWith("#/"))
            {
                var parts = refPath[2..].Split('/');
                var current = root;
                foreach (var part in parts)
                {
                    if (current.TryGetProperty(part, out var next))
                        current = next;
                    else
                        return element;
                }
                return current;
            }
        }
        return element;
    }

    private static object? GenerateFromSchema(JsonElement schema, JsonElement root, int depth, HashSet<string> visited)
    {
        if (depth > 5) return new Dictionary<string, object>();

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var refPath = refProp.GetString() ?? "";
            if (visited.Contains(refPath)) return new Dictionary<string, object>();
            visited.Add(refPath);
            schema = ResolveRef(schema, root);
        }

        if (schema.TryGetProperty("example", out var example))
            return JsonSerializer.Deserialize<object>(example.GetRawText());

        if (schema.TryGetProperty("allOf", out var allOf))
            return MergeAllOf(allOf, root, depth, visited);

        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.GetArrayLength() > 0)
            return GenerateFromSchema(ResolveRef(oneOf[0], root), root, depth + 1, visited);

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.GetArrayLength() > 0)
            return GenerateFromSchema(ResolveRef(anyOf[0], root), root, depth + 1, visited);

        var type = schema.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString() : null;

        if (type is null && schema.TryGetProperty("properties", out _))
            type = "object";

        return (type ?? "object") switch
        {
            "string" => schema.TryGetProperty("enum", out var enumProp)
                && enumProp.GetArrayLength() > 0
                    ? enumProp[0].GetString() ?? ""
                    : schema.TryGetProperty("format", out var fmt)
                        ? fmt.GetString() switch
                        {
                            "date-time" => "2024-01-01T00:00:00Z",
                            "date" => "2024-01-01",
                            "email" => "user@example.com",
                            "uri" or "url" => "https://example.com",
                            "uuid" => "00000000-0000-0000-0000-000000000000",
                            _ => ""
                        }
                        : "",
            "integer" or "number" => 0,
            "boolean" => false,
            "array" => GenerateArray(schema, root, depth, visited),
            "object" => GenerateObject(schema, root, depth, visited),
            _ => new Dictionary<string, object>()
        };
    }

    private static object MergeAllOf(JsonElement allOf, JsonElement root, int depth, HashSet<string> visited)
    {
        var merged = new Dictionary<string, object?>();
        foreach (var sub in allOf.EnumerateArray())
        {
            var resolved = ResolveRef(sub, root);
            var generated = GenerateFromSchema(resolved, root, depth + 1, visited);
            if (generated is Dictionary<string, object?> dict)
                foreach (var kv in dict)
                    merged.TryAdd(kv.Key, kv.Value);
        }
        return merged;
    }

    private static object GenerateArray(JsonElement schema, JsonElement root, int depth, HashSet<string> visited)
    {
        if (schema.TryGetProperty("items", out var items))
        {
            var resolved = ResolveRef(items, root);
            var item = GenerateFromSchema(resolved, root, depth + 1, visited);
            return new[] { item };
        }
        return Array.Empty<object>();
    }

    private static object GenerateObject(JsonElement schema, JsonElement root, int depth, HashSet<string> visited)
    {
        var obj = new Dictionary<string, object?>();

        if (schema.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateObject())
            {
                var resolved = ResolveRef(prop.Value, root);
                obj[prop.Name] = GenerateFromSchema(resolved, root, depth + 1, visited);
            }
        }

        if (obj.Count == 0 && schema.TryGetProperty("additionalProperties", out var addProps)
            && addProps.ValueKind == JsonValueKind.Object)
        {
            var resolved = ResolveRef(addProps, root);
            obj["key"] = GenerateFromSchema(resolved, root, depth + 1, visited);
        }

        return obj;
    }
}

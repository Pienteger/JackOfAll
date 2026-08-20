using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackOfAll.Lib.Services;

public record ApiRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValueItem> Headers { get; set; } = [];
    public List<KeyValueItem> Params { get; set; } = [];
    public string Body { get; set; } = "";
    public string BodyType { get; set; } = "none";
    public List<KeyValueItem> FormData { get; set; } = [];
    public string AuthType { get; set; } = "None";
    public string AuthValue { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ExtractionRule> ExtractionRules { get; set; } = [];
    public List<ApiResponseCheck> Checks { get; set; } = [];
}

public record KeyValueItem
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Kind { get; set; } = "text";
    public string FilePath { get; set; } = "";
    public string ContentType { get; set; } = "";
}

public record ApiResponse
{
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = "";
    public string Body { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
    public long ElapsedMs { get; set; }
    public long BodySizeBytes { get; set; }
    public bool IsBodyTruncated { get; set; }
    public int PreviewLimitBytes { get; set; }
    [JsonIgnore]
    public string? SavedBodyPath { get; set; }
}

public record ApiHistoryEntry
{
    public long Id { get; set; }
    public string Workspace { get; set; } = "";
    public string RequestName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string RequestJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public int StatusCode { get; set; }
    public long ElapsedMs { get; set; }
}

public static class ApiClientService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string CollectionPath = Path.Combine(AppPaths.Root, "requests.json");


    public static Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken ct = default) =>
        SendAsync(request, new ApiResponseReadOptions(), ct);

    public static async Task<ApiResponse> SendAsync(
        ApiRequest request,
        ApiResponseReadOptions readOptions,
        CancellationToken ct = default)
    {
        var resolvedUrl = ApiEnvironmentService.Resolve(request.Url);
        var resolvedParams = request.Params.Select(p => new KeyValueItem
        {
            Enabled = p.Enabled,
            Key = ApiEnvironmentService.Resolve(p.Key),
            Value = ApiEnvironmentService.Resolve(p.Value)
        }).ToList();
        var resolvedHeaders = request.Headers.Select(h => new KeyValueItem
        {
            Enabled = h.Enabled,
            Key = ApiEnvironmentService.Resolve(h.Key),
            Value = ApiEnvironmentService.Resolve(h.Value)
        }).ToList();

        var url = BuildUrl(resolvedUrl, resolvedParams);
        var method = new HttpMethod(request.Method);
        using var msg = new HttpRequestMessage(method, url);

        foreach (var h in resolvedHeaders.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        msg.Headers.UserAgent.TryParseAdd("JackOfAll/1.0");

        var resolvedAuthValue = ApiEnvironmentService.Resolve(request.AuthValue);
        switch (request.AuthType)
        {
            case "Bearer":
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolvedAuthValue);
                break;
            case "Basic":
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(resolvedAuthValue));
                msg.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                break;
        }

        if (method != HttpMethod.Get && method != HttpMethod.Head)
        {
            var resolvedFormData = request.FormData.Select(f => new KeyValueItem
            {
                Enabled = f.Enabled,
                Key = ApiEnvironmentService.Resolve(f.Key),
                Value = ApiEnvironmentService.Resolve(f.Value),
                Kind = f.Kind,
                FilePath = ApiEnvironmentService.Resolve(f.FilePath),
                ContentType = ApiEnvironmentService.Resolve(f.ContentType)
            }).ToList();
            var resolvedRequest = request with
            {
                Body = ApiEnvironmentService.Resolve(request.Body),
                Headers = resolvedHeaders,
                FormData = resolvedFormData
            };

            msg.Content = request.BodyType switch
            {
                "form-data" => BuildMultipartContent(resolvedFormData),
                "x-www-form-urlencoded" => BuildFormUrlEncodedContent(resolvedFormData),
                "raw" when !string.IsNullOrEmpty(resolvedRequest.Body) => BuildRawContent(resolvedRequest),
                _ => null
            };
        }

        var sw = Stopwatch.StartNew();
        using var response = await Client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseBody = await ApiResponseReader.ReadAsync(response.Content, readOptions, ct);
        sw.Stop();

        var headers = new Dictionary<string, string>();
        foreach (var h in response.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            headers[h.Key] = string.Join(", ", h.Value);

        return new ApiResponse
        {
            StatusCode = (int)response.StatusCode,
            StatusText = response.ReasonPhrase ?? "",
            Body = responseBody.Preview,
            Headers = headers,
            ElapsedMs = sw.ElapsedMilliseconds,
            BodySizeBytes = responseBody.SizeBytes,
            IsBodyTruncated = responseBody.IsTruncated,
            PreviewLimitBytes = readOptions.PreviewLimitBytes,
            SavedBodyPath = responseBody.SavedPath
        };
    }

    public static JsonDocument? TryParseResponseJson(ApiResponse response)
    {
        if (response.IsBodyTruncated) return null;
        try { return JsonDocument.Parse(response.Body); }
        catch (JsonException) { return null; }
    }

    public static Dictionary<string, string?> ApplyExtractionRules(
        ApiResponse response, List<ExtractionRule> rules)
    {
        using var json = rules.Any(rule => rule.Source == "jsonpath")
            ? TryParseResponseJson(response)
            : null;
        return ApplyExtractionRules(response, rules, json?.RootElement);
    }

    public static Dictionary<string, string?> ApplyExtractionRules(
        ApiResponse response, List<ExtractionRule> rules, JsonElement? jsonRoot)
    {
        var results = new Dictionary<string, string?>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Variable) || string.IsNullOrWhiteSpace(rule.Expression))
                continue;
            if (!ApiEnvironmentService.IsValidVariableName(rule.Variable))
                continue;

            string? value = rule.Source switch
            {
                "jsonpath" when jsonRoot is not null =>
                    ApiEnvironmentService.ExtractJsonPath(jsonRoot.Value, rule.Expression),
                "regex" when !response.IsBodyTruncated =>
                    ApiEnvironmentService.ExtractRegex(response.Body, rule.Expression),
                "header" => response.Headers.TryGetValue(rule.Expression, out var headerValue)
                    ? headerValue
                    : null,
                _ => null
            };

            results[rule.Variable] = value;
            if (value is not null)
                ApiEnvironmentService.SetVariable(rule.Variable, value);
        }
        return results;
    }
    public static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static HttpContent BuildMultipartContent(List<KeyValueItem> formData)
    {
        var content = new MultipartFormDataContent();
        try
        {
            foreach (var item in formData.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
            {
                if (item.Kind != "file")
                {
                    content.Add(new StringContent(item.Value, Encoding.UTF8), item.Key);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
                    throw new ApiRequestValidationException(
                        $"Multipart field '{item.Key}' references a missing file: {item.FilePath}");

                FileStream stream;
                try
                {
                    stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new ApiRequestValidationException(
                        $"Multipart field '{item.Key}' cannot read file: {item.FilePath}");
                }

                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(item.ContentType, out var contentType)
                    ? contentType
                    : new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, item.Key, Path.GetFileName(item.FilePath));
            }
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private static HttpContent BuildFormUrlEncodedContent(List<KeyValueItem> formData)
    {
        var pairs = formData
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => new KeyValuePair<string, string>(f.Key, f.Value));
        return new FormUrlEncodedContent(pairs);
    }

    private static HttpContent BuildRawContent(ApiRequest request)
    {
        var contentType = request.Headers
            .FirstOrDefault(h => h.Enabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        return new StringContent(request.Body, Encoding.UTF8,
            contentType?.Value ?? "application/json");
    }

    private static string BuildUrl(string url, List<KeyValueItem> queryParams)
    {
        var active = queryParams.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (active.Count == 0) return url;

        var sep = url.Contains('?') ? "&" : "?";
        var sb = new StringBuilder(url);
        foreach (var p in active)
        {
            sb.Append(sep);
            sb.Append(Uri.EscapeDataString(p.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(p.Value));
            sep = "&";
        }
        return sb.ToString();
    }

    public static List<ApiRequest> LoadCollection()
    {
        if (!File.Exists(CollectionPath)) return [];
        try
        {
            var json = File.ReadAllText(CollectionPath);
            return JsonSerializer.Deserialize<List<ApiRequest>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void SaveCollection(List<ApiRequest> requests)
    {
        var dir = Path.GetDirectoryName(CollectionPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CollectionPath, json);
    }

}

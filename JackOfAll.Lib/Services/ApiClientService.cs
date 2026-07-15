using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
}

public record KeyValueItem
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public record ApiResponse(
    int StatusCode,
    string StatusText,
    string Body,
    Dictionary<string, string> Headers,
    long ElapsedMs);

public static class ApiClientService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string CollectionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "JackOfAll", "requests.json");

    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "JackOfAll", "request-history.json");

    public static async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken ct = default)
    {
        var resolvedUrl = ApiEnvironmentService.Resolve(request.Url);
        var resolvedParams = request.Params.Select(p => new KeyValueItem
        {
            Enabled = p.Enabled,
            Key = ApiEnvironmentService.Resolve(p.Key),
            Value = ApiEnvironmentService.Resolve(p.Value)
        }).ToList();

        var url = BuildUrl(resolvedUrl, resolvedParams);
        var method = new HttpMethod(request.Method);
        using var msg = new HttpRequestMessage(method, url);

        foreach (var h in request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
            msg.Headers.TryAddWithoutValidation(
                ApiEnvironmentService.Resolve(h.Key),
                ApiEnvironmentService.Resolve(h.Value));

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
                Value = ApiEnvironmentService.Resolve(f.Value)
            }).ToList();
            var resolvedRequest = request with
            {
                Body = ApiEnvironmentService.Resolve(request.Body),
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
        using var response = await Client.SendAsync(msg, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        var headers = new Dictionary<string, string>();
        foreach (var h in response.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            headers[h.Key] = string.Join(", ", h.Value);

        return new ApiResponse(
            (int)response.StatusCode,
            response.ReasonPhrase ?? "",
            body,
            headers,
            sw.ElapsedMilliseconds);
    }

    public static Dictionary<string, string?> ApplyExtractionRules(
        ApiResponse response, List<ExtractionRule> rules)
    {
        var results = new Dictionary<string, string?>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Variable) || string.IsNullOrWhiteSpace(rule.Expression))
                continue;

            string? value = rule.Source switch
            {
                "jsonpath" => ApiEnvironmentService.ExtractJsonPath(response.Body, rule.Expression),
                "regex" => ApiEnvironmentService.ExtractRegex(response.Body, rule.Expression),
                "header" => response.Headers.TryGetValue(rule.Expression, out var hv) ? hv : null,
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
        foreach (var item in formData.Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
            content.Add(new StringContent(item.Value, Encoding.UTF8), item.Key);
        return content;
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

    public static List<ApiRequest> LoadHistory()
    {
        if (!File.Exists(HistoryPath)) return [];
        try
        {
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<ApiRequest>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void AddToHistory(ApiRequest request)
    {
        var history = LoadHistory();
        history.Insert(0, request with { Name = $"{request.Method} {request.Url}" });
        if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
        var dir = Path.GetDirectoryName(HistoryPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(HistoryPath, json);
    }
}

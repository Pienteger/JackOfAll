using System.Text;

namespace JackOfAll.Lib.Services;

public static class CurlExportService
{
    private const string Mask = "***";

    public static string ToBash(ApiRequest request, bool includeSecrets = false)
    {
        var builder = new StringBuilder("curl");
        builder.Append(" -X ").Append(request.Method.ToUpperInvariant());
        builder.Append(' ').Append(Quote(BuildUrl(request, includeSecrets)));

        foreach (var header in request.Headers.Where(IsActive))
        {
            var value = !includeSecrets && IsSensitiveName(header.Key) ? Mask : header.Value;
            AppendArgument(builder, "-H", $"{header.Key}: {value}");
        }

        if (request.AuthType == "Bearer")
            AppendArgument(builder, "-H", $"Authorization: Bearer {(includeSecrets ? request.AuthValue : Mask)}");
        else if (request.AuthType == "Basic")
            AppendArgument(builder, "-u", includeSecrets ? request.AuthValue : Mask);

        if (request.BodyType == "raw" && !string.IsNullOrEmpty(request.Body))
            AppendArgument(builder, "--data-raw", request.Body);
        else if (request.BodyType == "x-www-form-urlencoded")
        {
            foreach (var item in request.FormData.Where(IsActive))
                AppendArgument(builder, "--data-urlencode", $"{item.Key}={MaskIfSensitive(item, includeSecrets)}");
        }
        else if (request.BodyType == "form-data")
        {
            foreach (var item in request.FormData.Where(IsActive))
            {
                var value = item.Kind == "file"
                    ? $"{item.Key}=@{item.FilePath}{(string.IsNullOrWhiteSpace(item.ContentType) ? "" : $";type={item.ContentType}")}"
                    : $"{item.Key}={MaskIfSensitive(item, includeSecrets)}";
                AppendArgument(builder, "-F", value);
            }
        }

        return builder.ToString();
    }

    private static string BuildUrl(ApiRequest request, bool includeSecrets)
    {
        var active = request.Params.Where(IsActive).ToList();
        if (active.Count == 0) return request.Url;

        var builder = new StringBuilder(request.Url);
        var separator = request.Url.Contains('?') ? '&' : '?';
        foreach (var item in active)
        {
            builder.Append(separator)
                .Append(Uri.EscapeDataString(item.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(MaskIfSensitive(item, includeSecrets)));
            separator = '&';
        }
        return builder.ToString();
    }

    private static bool IsActive(KeyValueItem item) =>
        item.Enabled && !string.IsNullOrWhiteSpace(item.Key);

    private static string MaskIfSensitive(KeyValueItem item, bool includeSecrets) =>
        !includeSecrets && IsSensitiveName(item.Key) ? Mask : item.Value;

    private static bool IsSensitiveName(string name) =>
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("api-key", StringComparison.OrdinalIgnoreCase)
        || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("cookie", StringComparison.OrdinalIgnoreCase);

    private static void AppendArgument(StringBuilder builder, string option, string value) =>
        builder.Append(' ').Append(option).Append(' ').Append(Quote(value));

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}

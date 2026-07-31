using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public static class CurlParser
{
    public static bool IsCurl(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("curl.exe ", StringComparison.OrdinalIgnoreCase);
    }

    public static ApiRequest Parse(string curl)
    {
        var request = new ApiRequest { Method = "GET" };
        var tokens = Tokenize(curl);
        var dataParts = new List<string>();
        var getWithData = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var lower = t.ToLowerInvariant();

            if (lower is "curl" or "curl.exe")
                continue;

            if (t == "-G" || lower == "--get")
            {
                getWithData = true;
                continue;
            }

            if (t == "-I" || lower == "--head")
            {
                request.Method = "HEAD";
                continue;
            }

            if (IsNoValueOption(lower))
                continue;

            if (TryReadOptionValue(tokens, ref i, "--url", out var urlValue))
            {
                request.Url = urlValue;
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-X", "--request", out var method))
            {
                request.Method = method.ToUpperInvariant();
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-H", "--header", out var header))
            {
                ApplyHeader(request, header);
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-A", "--user-agent", out var userAgent))
            {
                AddOrReplaceHeader(request.Headers, "User-Agent", userAgent);
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-e", "--referer", out var referer))
            {
                AddOrReplaceHeader(request.Headers, "Referer", referer);
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-b", "--cookie", out var cookie))
            {
                if (!cookie.StartsWith('@'))
                    AddOrReplaceHeader(request.Headers, "Cookie", cookie);
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-u", "--user", out var user))
            {
                request.AuthType = "Basic";
                request.AuthValue = user;
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "--json", out var jsonBody))
            {
                if (request.Method == "GET") request.Method = "POST";
                request.BodyType = "raw";
                request.Body = AppendData(request.Body, jsonBody);
                AddOrReplaceHeader(request.Headers, "Content-Type", "application/json");
                AddOrReplaceHeader(request.Headers, "Accept", "application/json");
                continue;
            }

            if (IsDataOption(lower, out var urlEncoded) && i + 1 < tokens.Count)
            {
                var data = tokens[++i];
                if (request.Method == "GET" && !getWithData)
                    request.Method = "POST";

                if (getWithData)
                {
                    ParseFormData(data, request.Params);
                }
                else if (urlEncoded || request.BodyType == "x-www-form-urlencoded")
                {
                    request.BodyType = "x-www-form-urlencoded";
                    ParseFormData(data, request.FormData);
                }
                else
                {
                    request.BodyType = "raw";
                    dataParts.Add(data);
                    request.Body = string.Join("&", dataParts);
                }
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "-F", "--form", "--form-string", out var formField))
            {
                request.BodyType = "form-data";
                if (request.Method == "GET")
                    request.Method = "POST";
                AddFormField(formField, request.FormData);
                continue;
            }

            if (TryReadOptionValue(tokens, ref i, "--url-query", out var queryField))
            {
                ParseFormData(queryField, request.Params);
                continue;
            }

            if (IsIgnoredValueOption(lower) && i + 1 < tokens.Count)
            {
                i++;
                continue;
            }

            if (t.StartsWith("-"))
                continue;

            if (string.IsNullOrEmpty(request.Url))
                request.Url = t;
        }

        SplitQueryIntoParams(request);
        return request;
    }

    private static bool IsNoValueOption(string option) => option is
        "--location" or "-l" or "-g" or "-i" or "--include" or "--compressed" or "-s" or "--silent" or "-s" or "--show-error" or
        "-k" or "--insecure" or "-v" or "--verbose" or "-f" or "--fail" or "--fail-with-body" or
        "-n" or "--no-buffer" or "--http1.1" or "--http2" or "--http3" or "--globoff";

    private static bool IsIgnoredValueOption(string option) => option is
        "-o" or "--output" or "--connect-timeout" or "--max-time" or "-m" or "--retry" or
        "--retry-delay" or "-w" or "--write-out" or "--proxy" or "-x" or "--cert" or
        "--key" or "--cacert" or "-c" or "--cookie-jar" or "--request-target" or "--resolve" or
        "--connect-to" or "--interface";

    private static bool IsDataOption(string option, out bool urlEncoded)
    {
        urlEncoded = option is "--data-urlencode";
        return option is "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii" or "--data-urlencode";
    }

    private static bool TryReadOptionValue(List<string> tokens, ref int index, string option, out string value)
    {
        value = "";
        var token = tokens[index];
        if (token.Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= tokens.Count) return false;
            value = tokens[++index];
            return true;
        }

        var prefix = option + "=";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token[prefix.Length..];
            return true;
        }

        return false;
    }

    private static bool TryReadOptionValue(List<string> tokens, ref int index, string shortOption, string longOption, out string value) =>
        TryReadOptionValue(tokens, ref index, shortOption, out value)
        || TryReadOptionValue(tokens, ref index, longOption, out value);

    private static bool TryReadOptionValue(List<string> tokens, ref int index, string shortOption, string longOption, string longOption2, out string value) =>
        TryReadOptionValue(tokens, ref index, shortOption, out value)
        || TryReadOptionValue(tokens, ref index, longOption, out value)
        || TryReadOptionValue(tokens, ref index, longOption2, out value);

    private static void ApplyHeader(ApiRequest request, string header)
    {
        var colon = header.IndexOf(':');
        if (colon <= 0) return;

        var key = header[..colon].Trim();
        var val = header[(colon + 1)..].Trim();
        if (key.Length == 0) return;

        if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            if (val.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                request.BodyType = "x-www-form-urlencoded";
            else if (val.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                request.BodyType = "form-data";
        }

        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            if (val.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                request.AuthType = "Bearer";
                request.AuthValue = val[7..].Trim();
                return;
            }

            if (val.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                var token = val[6..].Trim();
                if (TryDecodeBasicToken(token, out var decoded))
                {
                    request.AuthType = "Basic";
                    request.AuthValue = decoded;
                    return;
                }
            }
        }

        AddOrReplaceHeader(request.Headers, key, val);
    }

    private static bool TryDecodeBasicToken(string token, out string decoded)
    {
        decoded = "";
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return decoded.Contains(':');
        }
        catch
        {
            return false;
        }
    }

    private static void AddOrReplaceHeader(List<KeyValueItem> headers, string key, string value)
    {
        var existing = headers.FirstOrDefault(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            headers.Add(new KeyValueItem { Enabled = true, Key = key, Value = value });
        else
            existing.Value = value;
    }

    private static string AppendData(string existing, string value) =>
        string.IsNullOrEmpty(existing) ? value : existing + "&" + value;

    private static void AddFormField(string formField, List<KeyValueItem> target)
    {
        var eq = formField.IndexOf('=');
        if (eq <= 0) return;
        target.Add(new KeyValueItem
        {
            Enabled = true,
            Key = formField[..eq],
            Value = formField[(eq + 1)..]
        });
    }

    private static void SplitQueryIntoParams(ApiRequest request)
    {
        if (string.IsNullOrEmpty(request.Url)) return;
        var qIdx = request.Url.IndexOf('?');
        if (qIdx < 0) return;

        var query = request.Url[(qIdx + 1)..];
        request.Url = request.Url[..qIdx];
        ParseFormData(query, request.Params);
    }

    private static void ParseFormData(string data, List<KeyValueItem> target)
    {
        foreach (var pair in data.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            target.Add(eq > 0
                ? new KeyValueItem { Enabled = true, Key = Decode(pair[..eq]), Value = Decode(pair[(eq + 1)..]) }
                : new KeyValueItem { Enabled = true, Key = Decode(pair), Value = "" });
        }
    }

    private static string Decode(string value) => WebUtility.UrlDecode(value) ?? value;

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        input = Regex.Replace(input, @"\\\s*\r?\n\s*", " ");
        input = Regex.Replace(input, @"\^\s*\r?\n\s*", " ");
        input = input.Replace("\r\n", " ").Replace("\n", " ");

        var i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i])) { i++; continue; }

            if (input[i] == '^' && i + 1 < input.Length && input[i + 1] == '"')
            {
                i += 2;
                var start = i;
                while (i < input.Length)
                {
                    if (input[i] == '^' && i + 1 < input.Length && input[i + 1] == '"')
                        break;
                    if (input[i] == '"' && (i + 1 >= input.Length || char.IsWhiteSpace(input[i + 1])))
                        break;
                    i++;
                }
                tokens.Add(input[start..i]);
                if (i < input.Length && input[i] == '^') i += 2;
                else if (i < input.Length && input[i] == '"') i++;
                continue;
            }

            if (input[i] == '$' && i + 1 < input.Length && input[i + 1] == '\'')
            {
                i += 2;
                var start = i;
                while (i < input.Length && input[i] != '\'')
                {
                    if (input[i] == '\\' && i + 1 < input.Length) i++;
                    i++;
                }
                var val = input[start..i];
                val = val.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\'", "'").Replace("\\\\", "\\");
                tokens.Add(val);
                if (i < input.Length) i++;
                continue;
            }

            if (input[i] == '\'' || input[i] == '"')
            {
                var quote = input[i++];
                var sb = new StringBuilder();
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length && quote == '"')
                    {
                        i++;
                        sb.Append(input[i++]);
                    }
                    else
                    {
                        sb.Append(input[i++]);
                    }
                }
                tokens.Add(sb.ToString());
                if (i < input.Length) i++;
                continue;
            }

            var wordStart = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;
            tokens.Add(input[wordStart..i]);
        }

        return tokens;
    }
}

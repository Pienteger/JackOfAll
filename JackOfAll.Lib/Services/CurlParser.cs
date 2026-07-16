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

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t is "curl" or "curl.exe")
                continue;

            if (t == "--location" || t == "-L" || t == "--compressed" || t == "-s" || t == "--silent"
                || t == "-S" || t == "--show-error" || t == "-k" || t == "--insecure" || t == "-v"
                || t == "--verbose" || t == "-i" || t == "--include" || t == "-f" || t == "--fail"
                || t == "--fail-with-body" || t == "-N" || t == "--no-buffer")
                continue;

            if ((t == "-X" || t == "--request") && i + 1 < tokens.Count)
            {
                request.Method = tokens[++i].ToUpperInvariant();
                continue;
            }

            if ((t == "-H" || t == "--header") && i + 1 < tokens.Count)
            {
                var header = tokens[++i];
                var colon = header.IndexOf(':');
                if (colon > 0)
                {
                    var key = header[..colon].Trim();
                    var val = header[(colon + 1)..].Trim();

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
                        }
                        else if (val.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                        {
                            request.AuthType = "Basic";
                            request.AuthValue = val[6..].Trim();
                        }
                        continue;
                    }

                    request.Headers.Add(new KeyValueItem { Enabled = true, Key = key, Value = val });
                }
                continue;
            }

            if ((t == "-d" || t == "--data" || t == "--data-raw" || t == "--data-binary"
                 || t == "--data-urlencode" || t == "--data-ascii") && i + 1 < tokens.Count)
            {
                var data = tokens[++i];
                if (request.Method == "GET")
                    request.Method = "POST";

                if (t == "--data-urlencode" || request.BodyType == "x-www-form-urlencoded")
                {
                    request.BodyType = "x-www-form-urlencoded";
                    ParseFormData(data, request.FormData);
                }
                else
                {
                    request.BodyType = "raw";
                    request.Body = data;
                }
                continue;
            }

            if ((t == "-F" || t == "--form") && i + 1 < tokens.Count)
            {
                request.BodyType = "form-data";
                if (request.Method == "GET")
                    request.Method = "POST";
                var formField = tokens[++i];
                var eq = formField.IndexOf('=');
                if (eq > 0)
                {
                    request.FormData.Add(new KeyValueItem
                    {
                        Enabled = true,
                        Key = formField[..eq],
                        Value = formField[(eq + 1)..]
                    });
                }
                continue;
            }

            if ((t == "-u" || t == "--user") && i + 1 < tokens.Count)
            {
                request.AuthType = "Basic";
                request.AuthValue = tokens[++i];
                continue;
            }

            if ((t == "-o" || t == "--output" || t == "--connect-timeout" || t == "--max-time"
                 || t == "-m" || t == "--retry" || t == "--retry-delay" || t == "-w"
                 || t == "--write-out" || t == "-e" || t == "--referer" || t == "-A"
                 || t == "--user-agent" || t == "--proxy" || t == "-x" || t == "--cert"
                 || t == "--key" || t == "--cacert" || t == "-b" || t == "--cookie"
                 || t == "-c" || t == "--cookie-jar") && i + 1 < tokens.Count)
            {
                if (t == "-A" || t == "--user-agent")
                {
                    request.Headers.Add(new KeyValueItem { Enabled = true, Key = "User-Agent", Value = tokens[++i] });
                    continue;
                }
                if (t == "-e" || t == "--referer")
                {
                    request.Headers.Add(new KeyValueItem { Enabled = true, Key = "Referer", Value = tokens[++i] });
                    continue;
                }
                i++;
                continue;
            }

            if (t.StartsWith("-"))
                continue;

            if (string.IsNullOrEmpty(request.Url))
                request.Url = t;
        }

        if (!string.IsNullOrEmpty(request.Url))
        {
            var uri = request.Url;
            var qIdx = uri.IndexOf('?');
            if (qIdx >= 0)
            {
                var query = uri[(qIdx + 1)..];
                request.Url = uri[..qIdx];
                foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eqIdx = pair.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        request.Params.Add(new KeyValueItem
                        {
                            Enabled = true,
                            Key = Uri.UnescapeDataString(pair[..eqIdx]),
                            Value = Uri.UnescapeDataString(pair[(eqIdx + 1)..])
                        });
                    }
                    else
                    {
                        request.Params.Add(new KeyValueItem { Enabled = true, Key = Uri.UnescapeDataString(pair), Value = "" });
                    }
                }
            }
        }

        return request;
    }

    private static void ParseFormData(string data, List<KeyValueItem> target)
    {
        foreach (var pair in data.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
            {
                target.Add(new KeyValueItem
                {
                    Enabled = true,
                    Key = Uri.UnescapeDataString(pair[..eq]),
                    Value = Uri.UnescapeDataString(pair[(eq + 1)..])
                });
            }
        }
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        // Normalize line continuations: backslash-newline and caret-newline (Windows)
        input = Regex.Replace(input, @"\\\s*\r?\n\s*", " ");
        input = Regex.Replace(input, @"\^\s*\r?\n\s*", " ");
        input = input.Replace("\r\n", " ").Replace("\n", " ");

        var i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i])) { i++; continue; }

            // Windows cmd: ^" used as quote delimiter (caret escapes the quote in cmd)
            if (input[i] == '^' && i + 1 < input.Length && input[i + 1] == '"')
            {
                i += 2;
                var start = i;
                // Read until ^" or end-of-string "
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

            if (input[i] == '\'' || input[i] == '"')
            {
                var quote = input[i++];
                var start = i;
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length && quote == '"')
                        i++;
                    i++;
                }
                tokens.Add(input[start..i].Replace("\\\"", "\"").Replace("\\'", "'"));
                if (i < input.Length) i++;
                continue;
            }

            // $'...' syntax (common in copied curls)
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

            var wordStart = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;
            tokens.Add(input[wordStart..i]);
        }

        return tokens;
    }
}

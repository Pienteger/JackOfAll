using System.Text;

namespace JackOfAll.Lib.Services;

public static class Base64Service
{
    public static (string? Result, string? Error) Encode(string input)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            return (Convert.ToBase64String(bytes), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public static (string? Result, string? Error) Decode(string input)
    {
        try
        {
            var bytes = Convert.FromBase64String(input.Trim());
            return (Encoding.UTF8.GetString(bytes), null);
        }
        catch (FormatException ex)
        {
            return (null, $"Invalid Base64: {ex.Message}");
        }
    }

    public static (string? Result, string? Error) UrlEncode(string input)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            return (Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('='), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public static (string? Result, string? Error) UrlDecode(string input)
    {
        try
        {
            var padded = input.Trim()
                .Replace('-', '+')
                .Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            var bytes = Convert.FromBase64String(padded);
            return (Encoding.UTF8.GetString(bytes), null);
        }
        catch (FormatException ex)
        {
            return (null, $"Invalid Base64URL: {ex.Message}");
        }
    }

    public static bool IsBase64(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        try
        {
            Convert.FromBase64String(trimmed);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JackOfAll.Lib.Services;

public static partial class JwtService
{
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JwtPattern();

    public static string? FindJwtInText(string text)
    {
        var match = JwtPattern().Match(text);
        if (!match.Success) return null;

        try
        {
            var parts = match.Value.Split('.');
            var headerJson = Base64UrlDecode(parts[0]);
            JsonDocument.Parse(headerJson);
            return match.Value;
        }
        catch
        {
            return null;
        }
    }

    public static JwtDecodeResult? Decode(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
                return null;

            var jwt = handler.ReadJwtToken(token);
            var headerJson = FormatJson(jwt.Header.SerializeToJson());
            var payloadJson = FormatJson(jwt.Payload.SerializeToJson());

            var expiry = jwt.Payload.Expiration is { } exp
                ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
                : (DateTime?)null;

            return new JwtDecodeResult(
                Header: headerJson,
                Payload: payloadJson,
                Signature: jwt.RawSignature,
                Expiry: expiry,
                IsExpired: expiry.HasValue && expiry.Value < DateTime.UtcNow,
                Algorithm: jwt.Header.Alg ?? "unknown",
                Issuer: jwt.Issuer,
                Subject: jwt.Subject
            );
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static string FormatJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}

public sealed record JwtDecodeResult(
    string Header,
    string Payload,
    string Signature,
    DateTime? Expiry,
    bool IsExpired,
    string Algorithm,
    string? Issuer,
    string? Subject);

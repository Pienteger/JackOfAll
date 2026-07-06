namespace JackOfAll.Lib.Services;

public static class TimestampService
{
    public static long NowUnixSeconds()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static long NowUnixMilliseconds()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static (string? Result, string? Error) FromUnixTimestamp(string input)
    {
        var trimmed = input.Trim();
        if (!long.TryParse(trimmed, out var value))
            return (null, "Not a valid number");

        try
        {
            DateTimeOffset dto;
            if (trimmed.Length >= 13)
                dto = DateTimeOffset.FromUnixTimeMilliseconds(value);
            else
                dto = DateTimeOffset.FromUnixTimeSeconds(value);

            var local = dto.ToLocalTime();
            var result = $"UTC:   {dto:yyyy-MM-dd HH:mm:ss.fff}\nLocal: {local:yyyy-MM-dd HH:mm:ss.fff zzz}\nISO:   {dto:O}";
            return (result, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, "Timestamp out of range");
        }
    }

    public static (string? Result, string? Error) ToUnixTimestamp(string input)
    {
        var trimmed = input.Trim();
        if (DateTimeOffset.TryParse(trimmed, out var dto))
        {
            var seconds = dto.ToUnixTimeSeconds();
            var millis = dto.ToUnixTimeMilliseconds();
            return ($"Seconds:      {seconds}\nMilliseconds: {millis}", null);
        }

        return (null, "Could not parse date. Try: 2024-01-15 10:30:00, or ISO 8601 format.");
    }

    public static string GetCurrentTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var local = now.ToLocalTime();
        return $"Unix (seconds):      {now.ToUnixTimeSeconds()}\n" +
               $"Unix (milliseconds): {now.ToUnixTimeMilliseconds()}\n" +
               $"UTC:                 {now:yyyy-MM-dd HH:mm:ss.fff}\n" +
               $"Local:               {local:yyyy-MM-dd HH:mm:ss.fff zzz}\n" +
               $"ISO 8601:            {now:O}";
    }
}

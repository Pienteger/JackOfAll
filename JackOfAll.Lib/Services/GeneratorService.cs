using System.Security.Cryptography;

namespace JackOfAll.Lib.Services;

public static class GeneratorService
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}|;:,.<>?";

    public static string GeneratePassword(int length, bool upper, bool lower, bool digits, bool symbols)
    {
        var pool = string.Concat(
            upper ? Upper : "",
            lower ? Lower : "",
            digits ? Digits : "",
            symbols ? Symbols : "");

        if (pool.Length == 0) pool = Lower + Digits;

        return RandomString(pool, length);
    }

    public static string GenerateGuid() => Guid.NewGuid().ToString("D");

    public static string GenerateGuidNoDashes() => Guid.NewGuid().ToString("N");

    public static string GenerateGuidUppercase() => Guid.NewGuid().ToString("D").ToUpperInvariant();

    private static string RandomString(string pool, int length)
    {
        var result = new char[length];
        for (var i = 0; i < length; i++)
            result[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        return new string(result);
    }
}

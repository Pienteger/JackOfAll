using System.Security.Cryptography;
using System.Text;

namespace JackOfAll.Lib.Services;

public static class HashService
{
    public static string ComputeMd5(string input)
        => ComputeHash(MD5.Create(), input);

    public static string ComputeSha1(string input)
        => ComputeHash(SHA1.Create(), input);

    public static string ComputeSha256(string input)
        => ComputeHash(SHA256.Create(), input);

    public static string ComputeSha512(string input)
        => ComputeHash(SHA512.Create(), input);

    private static string ComputeHash(HashAlgorithm algorithm, string input)
    {
        using (algorithm)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = algorithm.ComputeHash(bytes);
            return Convert.ToHexStringLower(hash);
        }
    }
}

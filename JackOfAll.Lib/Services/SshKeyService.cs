using System.Diagnostics;
using System.IO;

namespace JackOfAll.Lib.Services;

public sealed record SshKeyInfo(string Name, string PublicKeyPath, string PrivateKeyPath, string Type, string PublicKeyContent);

public static class SshKeyService
{
    private static string SshDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public static List<SshKeyInfo> ListKeys()
    {
        var dir = SshDir;
        if (!Directory.Exists(dir))
            return [];

        var pubFiles = Directory.GetFiles(dir, "*.pub");
        var keys = new List<SshKeyInfo>();

        foreach (var pubFile in pubFiles)
        {
            var name = Path.GetFileNameWithoutExtension(pubFile);
            var privateKeyPath = Path.Combine(dir, name);
            var content = File.ReadAllText(pubFile).Trim();
            var type = content.Split(' ') is [var algo, ..] ? algo : "unknown";

            keys.Add(new SshKeyInfo(name, pubFile, privateKeyPath, type, content));
        }

        return keys.OrderBy(k => k.Name).ToList();
    }

    public static string ReadPublicKey(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    public static async Task<(bool Success, string Output)> GenerateKeyAsync(
        string name, string type, string comment, string passphrase = "")
    {
        var keyPath = Path.Combine(SshDir, name);
        if (File.Exists(keyPath))
            return (false, $"Key '{name}' already exists.");

        Directory.CreateDirectory(SshDir);

        var args = $"-t {type} -f \"{keyPath}\" -C \"{comment}\" -N \"{passphrase}\"";
        var psi = new ProcessStartInfo("ssh-keygen", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "Failed to start ssh-keygen.");

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return proc.ExitCode == 0
                ? (true, $"Key generated: {keyPath}")
                : (false, string.IsNullOrEmpty(error) ? output : error);
        }
        catch (Exception ex)
        {
            return (false, $"ssh-keygen not found or failed: {ex.Message}");
        }
    }
}

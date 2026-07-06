using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackOfAll.Lib.Services;

public sealed class KubectlService
{
    private CancellationTokenSource? _logCts;

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var result = await RunAsync("version", "--client", "--output=json");
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> GetNamespacesAsync()
    {
        var result = await RunAsync("get", "namespaces", "-o=json");
        if (!result.Success) return ["default"];

        var doc = JsonNode.Parse(result.Output);
        var items = doc?["items"]?.AsArray() ?? [];
        return items
            .Select(i => i?["metadata"]?["name"]?.GetValue<string>())
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
    }

    public async Task<List<PodInfo>> GetPodsAsync(string ns)
    {
        var result = await RunAsync("get", "pods", $"-n={ns}", "-o=json");
        if (!result.Success) return [];

        var doc = JsonNode.Parse(result.Output);
        var items = doc?["items"]?.AsArray() ?? [];
        return items.Select(i =>
        {
            var meta = i?["metadata"];
            var status = i?["status"];
            var containerStatuses = status?["containerStatuses"]?.AsArray();
            var restarts = containerStatuses?
                .Sum(c => c?["restartCount"]?.GetValue<int>() ?? 0) ?? 0;

            return new PodInfo(
                Name: meta?["name"]?.GetValue<string>() ?? "",
                Namespace: ns,
                Status: status?["phase"]?.GetValue<string>() ?? "Unknown",
                Restarts: restarts,
                Age: DateTime.UtcNow - (meta?["creationTimestamp"]?.GetValue<DateTime>() ?? DateTime.UtcNow)
            );
        }).ToList();
    }

    public async Task<CliResult> RestartDeploymentAsync(string ns, string podName)
    {
        var deployName = GetDeploymentName(podName);
        return await RunAsync("rollout", "restart", $"deployment/{deployName}", $"-n={ns}");
    }

    public void StartLogStream(string ns, string podName, Action<string> onLine, int tailLines = 100)
    {
        StopLogStream();
        _logCts = new CancellationTokenSource();
        var ct = _logCts.Token;

        Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo("kubectl",
                    $"logs -f {podName} -n={ns} --tail={tailLines}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process is null) return;

                using var reg = ct.Register(() =>
                {
                    try { process.Kill(); } catch { }
                });

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
                {
                    if (ct.IsCancellationRequested) break;
                    onLine(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                onLine($"[ERROR] Log stream failed: {ex.Message}");
            }
        }, ct);
    }

    public void StopLogStream()
    {
        _logCts?.Cancel();
        _logCts?.Dispose();
        _logCts = null;
    }

    private static string GetDeploymentName(string podName)
    {
        var parts = podName.Split('-');
        return parts.Length > 2
            ? string.Join('-', parts[..^2])
            : podName;
    }

    private static async Task<CliResult> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("kubectl", string.Join(' ', args))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start kubectl");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var error = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        return new CliResult(process.ExitCode == 0, output, error);
    }
}

public sealed record PodInfo(
    string Name,
    string Namespace,
    string Status,
    int Restarts,
    TimeSpan Age)
{
    public string AgeDisplay => Age.TotalDays >= 1
        ? $"{(int)Age.TotalDays}d"
        : Age.TotalHours >= 1
            ? $"{(int)Age.TotalHours}h"
            : $"{(int)Age.TotalMinutes}m";
}

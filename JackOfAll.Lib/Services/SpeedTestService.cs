using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace JackOfAll.Lib.Services;

public enum SpeedTestPhase { Idle, Download, Upload, Done, Error }

/// <summary>
/// Download/upload throughput against Cloudflare's public speed-test endpoints, several parallel
/// streams for about 8 seconds each way. The first second is skipped to leave out TCP slow start.
/// </summary>
public sealed class SpeedTestService
{
    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=25000000"; // Cloudflare rejects much larger requests
    private const string UpUrl = "https://speed.cloudflare.com/__up";
    private const int UploadChunk = 8_000_000;
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = CreateClient();
    private long _bytes;

    public SpeedTestPhase Phase { get; private set; }
    public double DownMbps { get; private set; }
    public double UpMbps { get; private set; }
    public double LiveMbps { get; private set; }
    public string Error { get; private set; } = "";
    public DateTime FinishedAt { get; private set; } = DateTime.MinValue;
    public bool Running => Phase is SpeedTestPhase.Download or SpeedTestPhase.Upload;

    public async Task RunAsync(CancellationToken ct)
    {
        if (Running) return;
        DownMbps = UpMbps = LiveMbps = 0;
        Error = "";
        try
        {
            Phase = SpeedTestPhase.Download;
            DownMbps = await MeasureAsync(4, DownloadAsync, ct);
            Phase = SpeedTestPhase.Upload;
            UpMbps = await MeasureAsync(3, UploadAsync, ct);
            Phase = SpeedTestPhase.Done;
        }
        catch (OperationCanceledException)
        {
            Phase = SpeedTestPhase.Idle;
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
            Phase = SpeedTestPhase.Error;
        }
        finally
        {
            FinishedAt = DateTime.Now;
        }
    }

    private async Task<double> MeasureAsync(int streams, Func<CancellationToken, Task> body, CancellationToken outer)
    {
        Interlocked.Exchange(ref _bytes, 0);
        using var window = CancellationTokenSource.CreateLinkedTokenSource(outer);
        window.CancelAfter(Duration + TimeSpan.FromSeconds(1));
        var workers = Enumerable.Range(0, streams).Select(_ => RunStream(body, window.Token)).ToList();
        var all = Task.WhenAll(workers);

        var sw = Stopwatch.StartNew();
        long warmBytes = 0;
        double warmAt = 0;
        while (!all.IsCompleted)
        {
            await Task.WhenAny(all, Task.Delay(100, CancellationToken.None));
            var t = sw.Elapsed.TotalSeconds;
            var b = Interlocked.Read(ref _bytes);
            if (warmAt == 0 && t >= 1) { warmBytes = b; warmAt = t; }
            LiveMbps = warmAt > 0 && t > warmAt + 0.2 ? (b - warmBytes) * 8 / (t - warmAt) / 1e6
                : t > 0.2 ? b * 8 / t / 1e6 : 0;
        }
        outer.ThrowIfCancellationRequested();

        var secs = sw.Elapsed.TotalSeconds;
        var total = Interlocked.Read(ref _bytes);
        if (total == 0)
        {
            // every stream failed before sending anything: surface the first real error
            var failed = workers.FirstOrDefault(w => w.Result is not null)?.Result;
            throw failed as HttpRequestException ?? new HttpRequestException(failed?.Message ?? "No data transferred.");
        }
        return warmAt > 0 && secs > warmAt + 0.5 ? (total - warmBytes) * 8 / (secs - warmAt) / 1e6 : total * 8 / Math.Max(0.1, secs) / 1e6;
    }

    /// <summary>Runs one stream until the window closes; returns the error if it failed for another reason.</summary>
    private static async Task<Exception?> RunStream(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        try { await body(ct); return null; }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { return ct.IsCancellationRequested ? null : ex; }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        var buf = new byte[65536];
        while (!ct.IsCancellationRequested)
        {
            using var resp = await Http.GetAsync(DownUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            int n;
            while ((n = await s.ReadAsync(buf, ct)) > 0) Interlocked.Add(ref _bytes, n);
        }
    }

    private async Task UploadAsync(CancellationToken ct)
    {
        var buf = new byte[65536];
        Random.Shared.NextBytes(buf);
        while (!ct.IsCancellationRequested)
        {
            using var content = new CountingContent(buf, UploadChunk, n => Interlocked.Add(ref _bytes, n));
            using var resp = await Http.PostAsync(UpUrl, content, ct);
            resp.EnsureSuccessStatusCode();
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 16 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JackOfAll/1.0");
        return client;
    }

    /// <summary>Streams a repeated buffer as the request body and reports bytes as they leave.</summary>
    private sealed class CountingContent(byte[] chunk, long length, Action<int> onSent) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken ct)
        {
            for (long sent = 0; sent < length;)
            {
                var n = (int)Math.Min(chunk.Length, length - sent);
                await stream.WriteAsync(chunk.AsMemory(0, n), ct);
                sent += n;
                onSent(n);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long len)
        {
            len = length;
            return true;
        }
    }
}

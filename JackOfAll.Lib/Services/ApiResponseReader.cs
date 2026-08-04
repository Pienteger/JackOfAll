using System.IO;
using System.Net.Http;
using System.Text;

namespace JackOfAll.Lib.Services;

public sealed record ApiResponseReadOptions
{
    public const int DefaultPreviewLimitBytes = 10 * 1024 * 1024;

    public int PreviewLimitBytes { get; init; } = DefaultPreviewLimitBytes;
    public string? SaveFullResponsePath { get; init; }
}

public sealed record ApiResponseBody(
    string Preview,
    long SizeBytes,
    bool IsTruncated,
    string? SavedPath);

public static class ApiResponseReader
{
    private const int BufferSize = 81920;

    public static async Task<ApiResponseBody> ReadAsync(
        HttpContent content,
        ApiResponseReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.PreviewLimitBytes);

        var buffer = new byte[BufferSize];
        using var preview = new MemoryStream(Math.Min(options.PreviewLimitBytes, BufferSize));
        FileStream? output = null;
        var outputCreated = false;
        long sizeBytes = 0;

        try
        {
            if (!string.IsNullOrWhiteSpace(options.SaveFullResponsePath))
            {
                output = new FileStream(
                    options.SaveFullResponsePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                outputCreated = true;
            }

            await using var input = await content.ReadAsStreamAsync(cancellationToken);
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;

                sizeBytes += read;
                var previewRemaining = options.PreviewLimitBytes - (int)preview.Length;
                if (previewRemaining > 0)
                    preview.Write(buffer, 0, Math.Min(read, previewRemaining));

                if (output is not null)
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (output is not null)
                await output.FlushAsync(cancellationToken);

            var encoding = ResolveEncoding(content.Headers.ContentType?.CharSet);
            var text = encoding.GetString(preview.GetBuffer(), 0, (int)preview.Length);
            return new ApiResponseBody(
                text,
                sizeBytes,
                sizeBytes > options.PreviewLimitBytes,
                outputCreated ? options.SaveFullResponsePath : null);
        }
        catch
        {
            if (outputCreated && File.Exists(options.SaveFullResponsePath))
            {
                if (output is not null)
                {
                    await output.DisposeAsync();
                    output = null;
                }
                File.Delete(options.SaveFullResponsePath);
            }
            throw;
        }
        finally
        {
            if (output is not null)
                await output.DisposeAsync();
        }
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try { return Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }
}

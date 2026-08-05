using System.IO;
using System.Buffers;
using System.Text;

namespace JackOfAll.Lib.Services;

public sealed record LogFileIndex(
    string Path,
    Encoding Encoding,
    int PreambleBytes,
    IReadOnlyList<long> PageOffsets,
    long TotalLines,
    long ScannedBytes,
    long FileLength,
    int PageSize)
{
    public bool IsTruncated => ScannedBytes < FileLength;
    public int PageCount => Math.Max(1, PageOffsets.Count);
}

public sealed record LogPage(IReadOnlyList<LogLine> Lines, bool ShortenedLine);

public static class LogFileIndexService
{
    private const int BufferSize = 64 * 1024;
    private const int MaxPageBytes = 16 * 1024 * 1024;
    private const int MaxMessageChars = 256 * 1024;

    public static async Task<LogFileIndex> BuildAsync(
        string path,
        long scanLimitBytes,
        int pageSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var fileLength = stream.Length;
        var (encoding, preambleBytes) = await DetectEncodingAsync(stream, cancellationToken).ConfigureAwait(false);
        var scanEnd = Math.Min(fileLength, Math.Max(preambleBytes, scanLimitBytes));
        if (encoding.CodePage is 1200 or 1201 && ((scanEnd - preambleBytes) & 1) != 0)
            scanEnd--;

        var offsets = new List<long> { preambleBytes };
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long lines = 0;
        long position = preambleBytes;
        var endedWithNewline = false;
        stream.Position = preambleBytes;

        try
        {
            while (position < scanEnd)
            {
                var requested = (int)Math.Min(buffer.Length, scanEnd - position);
                var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;

                for (var index = 0; index < read; index++)
                {
                    var width = NewlineWidth(buffer, index, read, encoding.CodePage);
                    if (width == 0) continue;
                    lines++;
                    var nextOffset = position + index + width;
                    if (lines % pageSize == 0 && nextOffset < scanEnd)
                        offsets.Add(nextOffset);
                    endedWithNewline = nextOffset == scanEnd;
                    index += width - 1;
                }

                position += read;
                progress?.Report(scanEnd == 0 ? 1 : (double)position / scanEnd);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (position > preambleBytes && !endedWithNewline)
            lines++;

        return new LogFileIndex(path, encoding, preambleBytes, offsets, lines, position, fileLength, pageSize);
    }

    public static async Task<LogPage> ReadPageAsync(LogFileIndex index, int pageNumber, CancellationToken cancellationToken)
    {
        if (pageNumber < 0 || pageNumber >= index.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var start = index.PageOffsets[pageNumber];
        var end = pageNumber + 1 < index.PageOffsets.Count
            ? index.PageOffsets[pageNumber + 1]
            : index.ScannedBytes;
        var requested = end - start;
        var shortened = requested > MaxPageBytes;
        var length = (int)Math.Min(requested, MaxPageBytes);
        var bytes = new byte[length];

        await using var stream = new FileStream(index.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            BufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.Position = start;
        var total = 0;
        while (total < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        var text = index.Encoding.GetString(bytes, 0, total);
        var rawLines = text.ReplaceLineEndings("\n").Split('\n');
        var result = new List<LogLine>(Math.Min(rawLines.Length, index.PageSize));
        var baseLine = (long)pageNumber * index.PageSize + 1;
        for (var lineIndex = 0; lineIndex < rawLines.Length && result.Count < index.PageSize; lineIndex++)
        {
            var raw = rawLines[lineIndex];
            if (lineIndex == rawLines.Length - 1 && string.IsNullOrEmpty(raw)) continue;
            if (LogLineParser.IsContinuation(raw) && result.Count > 0)
            {
                var previous = result[^1];
                var combined = previous.Message + Environment.NewLine + raw.TrimEnd();
                if (combined.Length > MaxMessageChars)
                {
                    combined = combined[..MaxMessageChars] + "… [shortened]";
                    shortened = true;
                }
                result[^1] = previous with { Message = combined };
                continue;
            }

            var parsed = LogLineParser.Parse(raw, baseLine + lineIndex);
            if (parsed.Message.Length > MaxMessageChars)
            {
                parsed = parsed with { Message = parsed.Message[..MaxMessageChars] + "… [shortened]" };
                shortened = true;
            }
            result.Add(parsed);
        }

        return new LogPage(result, shortened);
    }

    private static int NewlineWidth(byte[] buffer, int index, int length, int codePage)
    {
        if (codePage == 1200)
            return index + 1 < length && buffer[index] == 0x0A && buffer[index + 1] == 0x00 ? 2 : 0;
        if (codePage == 1201)
            return index + 1 < length && buffer[index] == 0x00 && buffer[index + 1] == 0x0A ? 2 : 0;
        return buffer[index] == 0x0A ? 1 : 0;
    }

    private static async Task<(Encoding Encoding, int PreambleBytes)> DetectEncodingAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        var bom = new byte[3];
        var read = await stream.ReadAsync(bom, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return (new UTF8Encoding(false, false), 3);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return (new UnicodeEncoding(false, true, false), 2);
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return (new UnicodeEncoding(true, true, false), 2);
        return (new UTF8Encoding(false, false), 0);
    }
}

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JackOfAll.Lib.Services.Ferry;

/// <summary>
/// The small web server the phone talks to. It is a raw socket rather than HttpListener, so it needs no admin
/// rights or URL reservation. Every request except the expired-link page needs the secret token, given once in
/// the QR link and kept in a cookie after that.
/// </summary>
public sealed class FerryServer
{
    private const string LockedPage = "<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>Ferry</title>" +
        "<body style=\"font:17px/1.45 -apple-system,system-ui,sans-serif;background:#10121f;color:#eceef8;text-align:center;padding:72px 24px\">" +
        "<h1 style=\"font-size:22px\">This link doesn&rsquo;t work any more</h1>" +
        "<p style=\"color:#8f97b6\">Open Ferry in JackOfAll on your PC and scan the QR code again.</p>";

    private static readonly object FileGate = new();
    private readonly FerryHub _hub;
    private readonly FerrySettings _settings;
    private readonly byte[] _page;
    private TcpListener? _listener;
    private volatile bool _running;

    public int Port { get; private set; }

    public FerryServer(FerryHub hub, FerrySettings settings)
    {
        _hub = hub;
        _settings = settings;
        using var stream = typeof(FerryServer).Assembly.GetManifestResourceStream("ferry-page.html")
            ?? throw new InvalidOperationException("The Ferry phone page is missing from the build.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        _page = copy.ToArray();
    }

    /// <summary>Starts listening on the saved port or the next free one. Returns an error message, or null on success.</summary>
    public string? Start()
    {
        for (var port = _settings.Port; port < _settings.Port + 20 && _listener is null; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start(32);
                _listener = listener;
                Port = port;
            }
            catch (SocketException) { }
        }
        if (_listener is null) return $"Ferry couldn't open a network port between {_settings.Port} and {_settings.Port + 19}.";
        _running = true;
        new Thread(AcceptLoop) { IsBackground = true, Name = "ferry-accept" }.Start();
        return null;
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _hub.Wake();
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch { if (!_running) return; continue; }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "ferry-request" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                stream.ReadTimeout = 60_000;
                stream.WriteTimeout = 60_000;
                var request = FerryRequest.Read(stream);
                if (request is not null) Route(request, stream);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private void Route(FerryRequest r, NetworkStream s)
    {
        var token = r.Query("k") ?? r.Cookie("ferry");
        var authed = token is not null && token == _settings.Token;

        if (r.Path == "/")
        {
            if (!authed) { Send(s, 403, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(LockedPage)); return; }
            _hub.Seen(DeviceName(r.Header("user-agent")));
            Send(s, 200, "text/html; charset=utf-8", _page,
                $"Set-Cookie: ferry={_settings.Token}; Path=/; Max-Age=31536000; SameSite=Lax; HttpOnly\r\nReferrer-Policy: no-referrer\r\n");
            return;
        }
        if (!authed) { Error(s, 403, "locked"); return; }
        _hub.Seen(DeviceName(r.Header("user-agent")));

        if (r.Path == "/api/state" && r.Method == "GET")
        {
            int.TryParse(r.Query("since"), out var since);
            Send(s, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(_hub.WaitState(since, _running ? 20_000 : 0)));
        }
        else if (r.Path == "/api/upload" && r.Method is "PUT" or "POST") Upload(r, s);
        else if (r.Path == "/api/text" && r.Method == "POST") ReceiveText(r, s);
        else if (r.Path.StartsWith("/f/") || r.Path.StartsWith("/t/"))
        {
            var item = int.TryParse(r.Path[3..], out var id) ? _hub.Find(id) : null;
            if (item is null || item.IsText) { Error(s, 404, "gone"); return; }
            if (r.Path[1] == 'f') { SendFile(r, s, item); return; }
            item.EnsureThumb();
            if (item.ThumbJpeg is null) Error(s, 404, "no thumbnail");
            else Send(s, 200, "image/jpeg", item.ThumbJpeg, "Cache-Control: private, max-age=86400\r\n");
        }
        else Error(s, 404, "not found");
    }

    private void Upload(FerryRequest r, NetworkStream s)
    {
        if (!long.TryParse(r.Header("content-length"), out var length) || length < 0) { Error(s, 411, "Missing file size"); return; }
        var folder = _settings.Folder;
        try { Directory.CreateDirectory(folder); }
        catch { Error(s, 500, "Can't write to the folder on the PC"); return; }
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if (drive.AvailableFreeSpace < length + (64L << 20)) { Error(s, 507, "Not enough free space on the PC"); return; }
        }
        catch { /* network folders may not report free space */ }
        Continue(r, s);

        string path;
        FileStream file;
        lock (FileGate)
        {
            path = UniquePath(folder, SafeName(r.Query("name")));
            file = new FileStream(path + ".ferrypart", FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        }
        var ok = false;
        try
        {
            using (file)
            {
                var buffer = new byte[1 << 18];
                for (var left = length; left > 0;)
                {
                    var n = r.ReadBody(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) throw new IOException("upload cut off");
                    file.Write(buffer, 0, n);
                    left -= n;
                }
            }
            File.Move(path + ".ferrypart", path);
            ok = true;
        }
        finally
        {
            if (!ok) try { File.Delete(path + ".ferrypart"); } catch { }
        }

        // Keep the date the photo was taken rather than the time it arrived.
        if (long.TryParse(r.Query("mtime"), out var mtime) && mtime > 0)
            try { File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(mtime).UtcDateTime); } catch { }
        _hub.Add(new FerryItem { Name = Path.GetFileName(path), Path = path, Size = length, Device = DeviceName(r.Header("user-agent")) });
        Send(s, 200, "application/json; charset=utf-8", "{\"ok\":true}"u8.ToArray());
    }

    private void ReceiveText(FerryRequest r, NetworkStream s)
    {
        if (!long.TryParse(r.Header("content-length"), out var length) || length < 0) { Error(s, 411, "Missing length"); return; }
        if (length > 1 << 20) { Error(s, 413, "Text is too long; send it as a file"); return; }
        Continue(r, s);
        var body = new byte[length];
        for (var got = 0; got < length;)
        {
            var n = r.ReadBody(body, got, (int)length - got);
            if (n <= 0) return;
            got += n;
        }
        var text = Encoding.UTF8.GetString(body);
        if (string.IsNullOrWhiteSpace(text)) { Error(s, 400, "Nothing to send"); return; }
        _hub.Add(new FerryItem { IsText = true, Name = "Text", Text = text, Size = length, Device = DeviceName(r.Header("user-agent")) });
        Send(s, 200, "application/json; charset=utf-8", "{\"ok\":true}"u8.ToArray());
    }

    private void SendFile(FerryRequest r, NetworkStream s, FerryItem item)
    {
        FileStream file;
        try { file = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16); }
        catch { Error(s, 404, "The file is no longer on the PC"); return; }
        using (file)
        {
            long total = file.Length, start = 0, end = total - 1;
            var status = 200;
            // Ranges let the phone's browser play videos and resume downloads.
            var range = r.Header("range");
            if (range is not null && range.StartsWith("bytes=") && total > 0)
            {
                var spec = range[6..].Split(',')[0].Trim();
                var dash = spec.IndexOf('-');
                if (dash > 0 && long.TryParse(spec[..dash], out var a))
                {
                    if (a >= total) { Send(s, 416, "text/plain", [], $"Content-Range: bytes */{total}\r\n"); return; }
                    start = a;
                    if (long.TryParse(spec[(dash + 1)..], out var b) && b >= a) end = Math.Min(b, total - 1);
                    status = 206;
                }
                else if (dash == 0 && long.TryParse(spec[1..], out var suffix) && suffix > 0)
                {
                    start = Math.Max(0, total - suffix);
                    status = 206;
                }
            }

            var download = r.Query("dl") == "1" || !FerryMime.Inline(item.Name);
            var ascii = new string(item.Name.Select(static c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
            var headers = new StringBuilder();
            headers.Append("Accept-Ranges: bytes\r\n");
            headers.Append("Content-Security-Policy: sandbox\r\n");
            if (status == 206) headers.Append($"Content-Range: bytes {start}-{end}/{total}\r\n");
            headers.Append($"Content-Disposition: {(download ? "attachment" : "inline")}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(item.Name)}\r\n");
            var count = total == 0 ? 0 : end - start + 1;
            Head(s, status, FerryMime.Of(item.Name), count, headers.ToString());
            if (r.Method == "HEAD") return;

            file.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[1 << 18];
            for (var left = count; left > 0;)
            {
                var n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0) break;
                s.Write(buffer, 0, n);
                left -= n;
            }
            if (item.ToPhone && start == 0 && end == total - 1) _hub.Downloaded(item);
        }
    }

    // curl and some other clients wait for this before sending a large body; browsers never ask.
    private static void Continue(FerryRequest r, NetworkStream s)
    {
        if (string.Equals(r.Header("expect"), "100-continue", StringComparison.OrdinalIgnoreCase))
            s.Write("HTTP/1.1 100 Continue\r\n\r\n"u8);
    }

    private static void Error(NetworkStream s, int status, string message) =>
        Send(s, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(new { error = message }));

    private static void Send(NetworkStream s, int status, string type, byte[] body, string extra = "")
    {
        Head(s, status, type, body.Length, extra);
        s.Write(body);
    }

    private static void Head(NetworkStream s, int status, string type, long length, string extra)
    {
        var reason = status switch
        {
            200 => "OK", 206 => "Partial Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found",
            411 => "Length Required", 413 => "Payload Too Large", 416 => "Range Not Satisfiable", 507 => "Insufficient Storage",
            _ => "Error",
        };
        var cache = extra.Contains("Cache-Control:") ? "" : "Cache-Control: no-store\r\n";
        s.Write(Encoding.UTF8.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n{cache}{extra}\r\n"));
    }

    /// <summary>Strips folders and characters Windows won't accept, so a name from the phone can't escape the folder.</summary>
    public static string SafeName(string? name)
    {
        name = (name ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var bad = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => bad.Contains(c) || c < ' ' ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Trim('.').Length == 0) cleaned = "file";
        var stem = Path.GetFileNameWithoutExtension(cleaned).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "LPT1" or "LPT2" or "LPT3") cleaned = "_" + cleaned;
        if (cleaned.Length > 180) cleaned = cleaned[..170] + Path.GetExtension(cleaned);
        return cleaned;
    }

    /// <summary>Call under the file lock. Skips names taken by a finished file or an upload still in progress.</summary>
    public static string UniquePath(string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name), path = Path.Combine(folder, name);
        for (var i = 2; File.Exists(path) || File.Exists(path + ".ferrypart") || Directory.Exists(path); i++)
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    public static string DeviceName(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "your phone";
        if (userAgent.Contains("iPhone")) return "iPhone";
        if (userAgent.Contains("iPad")) return "iPad";
        if (userAgent.Contains("Android")) return userAgent.Contains("Mobile") ? "Android phone" : "Android tablet";
        if (userAgent.Contains("Macintosh")) return "Mac";
        if (userAgent.Contains("Windows")) return "Windows PC";
        if (userAgent.Contains("CrOS")) return "Chromebook";
        if (userAgent.Contains("Linux")) return "Linux PC";
        return "a browser";
    }
}

/// <summary>A parsed HTTP request: start line, query string, headers, and whatever body bytes arrived with the headers.</summary>
internal sealed class FerryRequest
{
    private readonly Dictionary<string, string> _query = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _headers = new(StringComparer.Ordinal);
    private byte[] _buffer = [];
    private int _position, _length;
    private Stream _stream = Stream.Null;

    public string Method { get; private set; } = "";
    public string Path { get; private set; } = "";

    public string? Query(string key) => _query.GetValueOrDefault(key);
    public string? Header(string key) => _headers.GetValueOrDefault(key);

    public string? Cookie(string name)
    {
        foreach (var part in (Header("cookie") ?? "").Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim() == name) return part[(eq + 1)..].Trim();
        }
        return null;
    }

    public int ReadBody(byte[] target, int offset, int count)
    {
        if (_position >= _length) return _stream.Read(target, offset, count);
        var n = Math.Min(count, _length - _position);
        Buffer.BlockCopy(_buffer, _position, target, offset, n);
        _position += n;
        return n;
    }

    public static FerryRequest? Read(Stream stream)
    {
        var buffer = new byte[32768];
        int length = 0, end = -1;
        while (end < 0)
        {
            if (length == buffer.Length) return null; // headers too large
            var n = stream.Read(buffer, length, buffer.Length - length);
            if (n <= 0) return null;
            var from = Math.Max(0, length - 3);
            length += n;
            var found = buffer.AsSpan(from, length - from).IndexOf("\r\n\r\n"u8);
            if (found >= 0) end = from + found;
        }

        var lines = Encoding.UTF8.GetString(buffer, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var request = new FerryRequest { Method = first[0].ToUpperInvariant(), _buffer = buffer, _position = end + 4, _length = length, _stream = stream };
        var target = first[1];
        var q = target.IndexOf('?');
        request.Path = q >= 0 ? target[..q] : target;
        if (q >= 0)
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                request._query[Decode(eq >= 0 ? pair[..eq] : pair)] = eq >= 0 ? Decode(pair[(eq + 1)..]) : "";
            }
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) request._headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        return request;
    }

    private static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch { return value; }
    }
}

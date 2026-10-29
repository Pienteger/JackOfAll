using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using JackOfAll.Lib;
using Microsoft.Data.Sqlite;

namespace JackOfAll.UserControls.Services.Tally;

/// <summary>One stretch of time spent in one window.</summary>
public sealed record TallySpan(DateTime Start, DateTime End, string App, string Exe, string Title)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Groups activity into a project when the app, the window title or either one contains <see cref="Contains"/>.</summary>
public sealed class TallyRule
{
    public string Project { get; set; } = "";
    public string Field { get; set; } = TallyRules.AnyField;
    public string Contains { get; set; } = "";
}

public static class TallyRules
{
    public const string AnyField = "App or title";
    public const string AppField = "App";
    public const string TitleField = "Title";
    public const string NoProject = "(no project)";
    public static readonly string[] Fields = [AnyField, AppField, TitleField];

    private static readonly string FilePath = Path.Combine(AppPaths.Root, "tally-rules.json");

    public static List<TallyRule> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<TallyRule>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<TallyRule> rules)
    {
        AppPaths.EnsureRoot();
        var clean = rules.Where(r => !string.IsNullOrWhiteSpace(r.Project) && !string.IsNullOrWhiteSpace(r.Contains)).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string ProjectOf(TallySpan span, IReadOnlyList<TallyRule> rules)
    {
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Contains) || string.IsNullOrWhiteSpace(r.Project)) continue;
            var inApp = span.App.Contains(r.Contains, StringComparison.OrdinalIgnoreCase)
                        || span.Exe.Contains(r.Contains, StringComparison.OrdinalIgnoreCase);
            var inTitle = span.Title.Contains(r.Contains, StringComparison.OrdinalIgnoreCase);
            var hit = r.Field switch
            {
                AppField => inApp,
                TitleField => inTitle,
                _ => inApp || inTitle
            };
            if (hit) return r.Project.Trim();
        }
        return NoProject;
    }
}

/// <summary>
/// Tally: records the window in front once a second and stops counting when you go idle or lock the PC.
/// Everything stays in a SQLite file under %LocalAppData%\JackOfAll; there is no start or stop button.
/// </summary>
public sealed class TallyRecorder : IDisposable
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(15);
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "iexplore"];

    public static readonly string DbPath = Path.Combine(AppPaths.Root, "tally.db");

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (string Path, DateTime Seen)> _pidPaths = new();
    private Current? _current;
    private DateTime _lastFlush = DateTime.UtcNow;
    private bool _started, _dbReady;

    public bool IsIdle { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised on the UI thread when a span is closed or the idle state changes.</summary>
    public event Action? Changed;

    private sealed class Current
    {
        public long Id;
        public DateTime Start, End;
        public string App = "", Exe = "", Title = "";
    }

    public void Start(Window mainWindow)
    {
        if (_started) return;
        _started = true;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Application.Current.Exit += (_, _) => Dispose();
    }

    public void Dispose()
    {
        _timer.Stop();
        Close(DateTime.UtcNow);
    }

    private static readonly Lazy<List<string>> BrowserProfiles = new(ReadBrowserProfiles);

    /// <summary>Profile names from Chromium browsers' "Local State", longest first.</summary>
    private static List<string> ReadBrowserProfiles()
    {
        var names = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var dir in new[] { @"Microsoft\Edge", @"Google\Chrome", @"BraveSoftware\Brave-Browser", @"Vivaldi" })
        {
            try
            {
                var file = Path.Combine(local, dir, "User Data", "Local State");
                if (!File.Exists(file)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("profile", out var p) || !p.TryGetProperty("info_cache", out var cache)) continue;
                foreach (var prof in cache.EnumerateObject())
                    if (prof.Value.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                        names.Add(name);
            }
            catch { }
        }
        return names.Distinct().OrderByDescending(n => n.Length).ToList();
    }

    public static bool IsBrowser(string exe) => Browsers.Contains(exe, StringComparer.OrdinalIgnoreCase);

    /// <summary>The site a browser tab shows, read from the window title ("Page - Site - Google Chrome" gives "Site").</summary>
    public static string SiteOf(TallySpan span)
    {
        if (!IsBrowser(span.Exe)) return span.App;
        var title = span.Title;
        var cut = title.LastIndexOf(" - ", StringComparison.Ordinal);
        var dash = title.LastIndexOf(" — ", StringComparison.Ordinal);
        cut = Math.Max(cut, dash);
        if (cut > 0) title = title[..cut]; // drop the browser's own name
        foreach (var profile in BrowserProfiles.Value) // Edge adds " - Personal" etc. when there is more than one profile
            if (title.EndsWith(" - " + profile, StringComparison.Ordinal)) { title = title[..^(profile.Length + 3)]; break; }
        foreach (var sep in new[] { " - ", " – ", " — ", " | ", " · " })
        {
            var i = title.LastIndexOf(sep, StringComparison.Ordinal);
            if (i > 0) title = title[(i + sep.Length)..];
        }
        title = title.Trim();
        return title.Length == 0 ? $"{span.App} (new tab)" : title;
    }

    // ── sampling ───────────────────────────────────────────────

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var idleFor = IdleTime();
        var hwnd = GetForegroundWindow();
        var (exe, path) = hwnd == 0 ? ("", "") : ProcessOf(hwnd);
        var locked = hwnd == 0 || exe.Equals("LockApp", StringComparison.OrdinalIgnoreCase);

        if (locked || idleFor >= IdleAfter)
        {
            var end = locked ? now : now - idleFor;
            if (_current is not null) Close(end > _current.Start ? end : _current.End);
            SetIdle(true);
            return;
        }
        SetIdle(false);

        var title = WindowTitle(hwnd);
        var app = AppName(exe, path, title);

        if (_current is not null && _current.App == app && _current.Title == title && now - _current.End <= MaxGap)
        {
            _current.End = now;
        }
        else
        {
            Close(now);
            _current = new Current { Start = now, End = now, App = app, Exe = exe, Title = title };
        }

        if (now - _lastFlush >= FlushEvery) Flush();
    }

    private void SetIdle(bool idle)
    {
        if (IsIdle == idle) return;
        IsIdle = idle;
        Changed?.Invoke();
    }

    private void Close(DateTime end)
    {
        if (_current is null) return;
        _current.End = end;
        Write(_current);
        _current = null;
        Changed?.Invoke();
    }

    /// <summary>Writes the open span so queries see it.</summary>
    public void Flush()
    {
        _lastFlush = DateTime.UtcNow;
        if (_current is not null) Write(_current);
    }

    private static string WindowTitle(nint hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private (string Exe, string Path) ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return ("", "");
        var now = DateTime.UtcNow;
        if (!_pidPaths.TryGetValue((int)pid, out var hit) || now - hit.Seen > TimeSpan.FromMinutes(1))
        {
            hit = (ImagePath(pid), now);
            _pidPaths[(int)pid] = hit;
            if (_pidPaths.Count > 500) _pidPaths.Clear();
        }
        var exe = hit.Path.Length > 0 ? Path.GetFileNameWithoutExtension(hit.Path) : SafeProcessName((int)pid);
        return (exe, hit.Path);
    }

    private static string SafeProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch { return ""; }
    }

    private string AppName(string exe, string path, string title)
    {
        // Store apps all run inside ApplicationFrameHost; their title is the app's name.
        if (exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && title.Length > 0)
            return title;
        if (exe.Length == 0) return "Unknown";
        if (_appNames.TryGetValue(exe, out var name)) return name;
        name = exe;
        try
        {
            if (path.Length > 0)
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(desc)) name = desc;
            }
        }
        catch { }
        _appNames[exe] = name;
        return name;
    }

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var idleMs = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(idleMs);
    }

    private static string ImagePath(uint pid)
    {
        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0) return "";
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    // ── storage ────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        AppPaths.EnsureRoot();
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        if (!_dbReady)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS spans (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    start_ms INTEGER NOT NULL,
                    end_ms INTEGER NOT NULL,
                    app TEXT NOT NULL,
                    exe TEXT NOT NULL,
                    title TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_spans_start ON spans (start_ms);
                CREATE INDEX IF NOT EXISTS idx_spans_end ON spans (end_ms);
                """;
            cmd.ExecuteNonQuery();
            _dbReady = true;
        }
        return conn;
    }

    private void Write(Current c)
    {
        if (c.Id == 0 && c.End - c.Start < TimeSpan.FromSeconds(1)) return;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            if (c.Id == 0)
            {
                cmd.CommandText = "INSERT INTO spans (start_ms, end_ms, app, exe, title) VALUES ($s, $e, $a, $x, $t); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$s", ToMs(c.Start));
                cmd.Parameters.AddWithValue("$e", ToMs(c.End));
                cmd.Parameters.AddWithValue("$a", c.App);
                cmd.Parameters.AddWithValue("$x", c.Exe);
                cmd.Parameters.AddWithValue("$t", c.Title);
                c.Id = (long)cmd.ExecuteScalar()!;
            }
            else
            {
                cmd.CommandText = "UPDATE spans SET end_ms = $e WHERE id = $id";
                cmd.Parameters.AddWithValue("$e", ToMs(c.End));
                cmd.Parameters.AddWithValue("$id", c.Id);
                cmd.ExecuteNonQuery();
            }
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Spans overlapping [from, to) in local time, clipped to that range.</summary>
    public List<TallySpan> Query(DateTime fromLocal, DateTime toLocal)
    {
        Flush();
        var from = fromLocal.ToUniversalTime();
        var to = toLocal.ToUniversalTime();
        var list = new List<TallySpan>();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT start_ms, end_ms, app, exe, title FROM spans WHERE end_ms > $f AND start_ms < $t ORDER BY start_ms";
            cmd.Parameters.AddWithValue("$f", ToMs(from));
            cmd.Parameters.AddWithValue("$t", ToMs(to));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var s = FromMs(r.GetInt64(0));
                var e = FromMs(r.GetInt64(1));
                if (s < from) s = from;
                if (e > to) e = to;
                if (e <= s) continue;
                list.Add(new TallySpan(s.ToLocalTime(), e.ToLocalTime(), r.GetString(2), r.GetString(3), r.GetString(4)));
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        return list;
    }

    /// <summary>Deletes everything recorded between the two local times.</summary>
    public void Forget(DateTime fromLocal, DateTime toLocal)
    {
        if (_current is not null && _current.Start.ToLocalTime() < toLocal && _current.End.ToLocalTime() > fromLocal)
            _current = null;
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM spans WHERE start_ms >= $f AND start_ms < $t";
        cmd.Parameters.AddWithValue("$f", ToMs(fromLocal.ToUniversalTime()));
        cmd.Parameters.AddWithValue("$t", ToMs(toLocal.ToUniversalTime()));
        cmd.ExecuteNonQuery();
        Changed?.Invoke();
    }

    private static long ToMs(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    // ── native ─────────────────────────────────────────────────

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
}

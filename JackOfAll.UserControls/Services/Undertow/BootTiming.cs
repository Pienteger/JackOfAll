using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using JackOfAll.Lib;

namespace JackOfAll.UserControls.Services.Undertow;

/// <summary>Runs a command, asking for administrator rights through UAC when the app is not already elevated.</summary>
public static class Elevated
{
    public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static void Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = IsAdmin ? "" : "runas"
        };
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}.");
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(file)} failed with exit code {p.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("You cancelled the administrator prompt.");
        }
    }
}

public sealed record BootPhase(string Name, double Ms);

public sealed record BootCulprit(string Kind, string Name, string FriendlyName, double TotalMs, double SlowedByMs)
{
    public string Display => string.IsNullOrWhiteSpace(FriendlyName) || FriendlyName == Name ? Name : $"{FriendlyName} ({Name})";
}

public sealed class BootRecord
{
    public DateTime When { get; init; }
    public double BootMs { get; init; }
    public double MainPathMs { get; init; }
    public double PostBootMs { get; init; }
    public int StartupApps { get; init; }
    public List<BootPhase> Phases { get; init; } = [];
    public List<BootCulprit> Culprits { get; } = [];

    public string WhenText => When.ToString("ddd d MMM yyyy, HH:mm");
    public string BootText => Seconds(BootMs);
    public string MainPathText => Seconds(MainPathMs);
    public string PostBootText => Seconds(PostBootMs);
    public string SlowText => Culprits.Count == 0 ? "" : $"{Culprits.Count} slowed it";

    public static string Seconds(double ms) => ms <= 0 ? "" : $"{ms / 1000:0.0} s";
}

/// <summary>
/// Reads the boot timing Windows writes to Microsoft-Windows-Diagnostics-Performance/Operational:
/// event 100 for each boot, events 101-110 for what slowed it down. Only administrators may read that log,
/// so the read goes through one elevated wevtutil call and the result is cached for the next visit.
/// </summary>
public static class BootTiming
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private static readonly string CachePath = Path.Combine(AppPaths.Root, "undertow-boot.xml");
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static readonly (string Field, string Label)[] PhaseFields =
    [
        ("BootKernelInitTime", "Kernel start"),
        ("BootDriverInitTime", "Drivers"),
        ("BootDevicesInitTime", "Devices"),
        ("BootPrefetchInitTime", "Prefetch"),
        ("BootAutoChkTime", "Disk check"),
        ("BootSmssInitTime", "Session manager"),
        ("BootCriticalServicesInitTime", "Critical services"),
        ("BootUserProfileProcessingTime", "User profile"),
        ("BootMachineProfileProcessingTime", "Machine profile"),
        ("BootExplorerInitTime", "Desktop (Explorer)"),
        ("BootPostBootTime", "After the desktop shows")
    ];

    public static DateTime LastBoot => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    public static DateTime? CachedAt => File.Exists(CachePath) ? File.GetLastWriteTime(CachePath) : null;

    public static List<BootRecord> LoadCached()
    {
        try { return File.Exists(CachePath) ? Parse(File.ReadAllText(CachePath)) : []; }
        catch { return []; }
    }

    /// <summary>Reads the log (asking for admin rights if needed), caches the XML and returns the boots, newest first.</summary>
    public static List<BootRecord> Read()
    {
        AppPaths.EnsureRoot();
        var tmp = Path.Combine(Path.GetTempPath(), $"undertow-{Guid.NewGuid():N}.xml");
        try
        {
            var query = "*[System[(EventID>=100 and EventID<=110)]]";
            Elevated.Run("cmd.exe", $"/c wevtutil qe \"{LogName}\" /q:\"{query}\" /f:xml /c:600 /rd:true /e:Events > \"{tmp}\"");
            if (!File.Exists(tmp)) throw new InvalidOperationException("wevtutil wrote nothing.");
            var xml = File.ReadAllText(tmp, Encoding.UTF8);
            var boots = Parse(xml);
            File.WriteAllText(CachePath, xml, Encoding.UTF8);
            return boots;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static List<BootRecord> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var events = doc.Root?.Elements(Ns + "Event").Select(e =>
        {
            var sys = e.Element(Ns + "System");
            var id = (int?)sys?.Element(Ns + "EventID") ?? 0;
            var whenAttr = sys?.Element(Ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
            var when = DateTime.TryParse(whenAttr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var w)
                ? w.ToLocalTime() : DateTime.MinValue;
            var data = e.Element(Ns + "EventData")?.Elements(Ns + "Data")
                .Where(d => d.Attribute("Name") is not null)
                .GroupBy(d => d.Attribute("Name")!.Value)
                .ToDictionary(g => g.Key, g => g.First().Value) ?? new Dictionary<string, string>();
            return (Id: id, When: when, Data: data);
        }).ToList() ?? [];

        var boots = events.Where(e => e.Id == 100).Select(e => (e.When, Record: new BootRecord
        {
            When = e.When,
            BootMs = Num(e.Data, "BootTime"),
            MainPathMs = Num(e.Data, "MainPathBootTime"),
            PostBootMs = Num(e.Data, "BootPostBootTime"),
            StartupApps = (int)Num(e.Data, "BootNumStartupApps"),
            Phases = PhaseFields.Select(p => new BootPhase(p.Label, Num(e.Data, p.Field))).Where(p => p.Ms > 0).ToList()
        })).OrderByDescending(b => b.When).ToList();

        // Degradation events carry no boot id; they are written a few minutes around the boot's own event.
        foreach (var e in events.Where(e => e.Id is > 100 and <= 110))
        {
            var nearest = boots.OrderBy(b => Math.Abs((b.When - e.When).TotalMinutes)).FirstOrDefault();
            if (nearest.Record is null || Math.Abs((nearest.When - e.When).TotalMinutes) > 15) continue;
            nearest.Record.Culprits.Add(new BootCulprit(
                KindOf(e.Id),
                Str(e.Data, "Name"),
                Str(e.Data, "FriendlyName"),
                Num(e.Data, "TotalTime"),
                Num(e.Data, "DegradationTime")));
        }
        foreach (var b in boots) b.Record.Culprits.Sort((x, y) => y.SlowedByMs.CompareTo(x.SlowedByMs));
        return boots.Select(b => b.Record).ToList();
    }

    private static string KindOf(int id) => id switch
    {
        101 => "App",
        102 => "Driver",
        103 => "Service",
        104 => "Disk cache",
        105 => "Core system",
        106 => "Background optimization",
        107 => "Machine policy",
        108 => "User policy",
        109 => "Device",
        110 => "Session manager",
        _ => $"Event {id}"
    };

    private static double Num(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string Str(Dictionary<string, string> d, string key) => d.TryGetValue(key, out var v) ? v.Trim() : "";
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using JackOfAll.Lib;
using Microsoft.Win32;

namespace JackOfAll.UserControls.Services.Undertow;

public sealed class StartupApp
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string ExePath { get; init; } = "";
    public bool Enabled { get; init; }
    public string SlowedBoot { get; set; } = "";
    /// <summary>"HKCU|subkey|value" pointing at the StartupApproved entry Task Manager also uses.</summary>
    public string ApprovedKey { get; init; } = "";

    public string StateText => Enabled ? "On" : "Off";
    public string ActionText => Enabled ? "Turn off" : "Turn on";
}

public sealed class ServiceItem
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string StartMode { get; init; } = "";
    public string Status { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsMicrosoft { get; init; }

    public bool IsAutomatic => StartMode.StartsWith(ServiceModes.Automatic, StringComparison.Ordinal);
    public string ActionText => IsAutomatic ? "Make manual" : "Make automatic";
}

public sealed class TaskItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Trigger { get; init; } = "";
    public string Action { get; init; } = "";
    public string Author { get; init; } = "";
    public bool Enabled { get; init; }
    public bool IsMicrosoft => Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

    public string StateText => Enabled ? "On" : "Off";
    public string ActionText => Enabled ? "Turn off" : "Turn on";
}

public sealed class ChangeEntry
{
    public DateTime When { get; set; }
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public bool Undone { get; set; }

    public string WhenText => When.ToString("d MMM, HH:mm");
    public string ChangeText => $"{Before} → {After}";
    public string ActionText => Undone ? "Undone" : "Undo";
    public bool CanUndo => !Undone;
}

public static class ServiceModes
{
    public const string Automatic = "Automatic";
    public const string Delayed = "Automatic (delayed)";
    public const string Manual = "Manual";
    public const string Disabled = "Disabled";
}

/// <summary>
/// Startup apps, auto-start services and logon/boot scheduled tasks: list them, turn them off and back on.
/// Every change goes into a change log so it can be undone later, including after a restart.
/// </summary>
public static class StartupAudit
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private static readonly string ChangesPath = Path.Combine(AppPaths.Root, "undertow-changes.json");
    private static readonly Dictionary<string, string> Companies = new(StringComparer.OrdinalIgnoreCase);

    // ── startup apps ───────────────────────────────────────────

    public static List<StartupApp> GetStartupApps()
    {
        var list = new List<StartupApp>();
        AddRun(list, Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "Registry, this user");
        AddRun(list, Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "Registry, all users");
        AddRun(list, Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "Registry, all users (32-bit)");
        AddFolder(list, Registry.CurrentUser, "HKCU", Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder, this user");
        AddFolder(list, Registry.LocalMachine, "HKLM", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder, all users");
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddRun(List<StartupApp> list, RegistryKey hive, string hiveName, string path, string approvedName, string label)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            var cmd = key.GetValue(name)?.ToString() ?? "";
            var exe = ExePathOf(cmd);
            list.Add(new StartupApp
            {
                Name = name,
                Command = cmd,
                Location = label,
                ExePath = exe,
                Publisher = CompanyOf(exe),
                Enabled = IsApproved(hive, approvedName, name),
                ApprovedKey = $"{hiveName}|{Approved}{approvedName}|{name}"
            });
        }
    }

    private static void AddFolder(List<StartupApp> list, RegistryKey hive, string hiveName, string folder, string label)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var (target, args) = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : (file, "");
            list.Add(new StartupApp
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Command = $"{target} {args}".Trim(),
                Location = label,
                ExePath = target,
                Publisher = CompanyOf(target),
                Enabled = IsApproved(hive, "StartupFolder", fileName),
                ApprovedKey = $"{hiveName}|{Approved}StartupFolder|{fileName}"
            });
        }
    }

    private static bool IsApproved(RegistryKey hive, string approvedName, string valueName)
    {
        using var key = hive.OpenSubKey(Approved + approvedName);
        return key?.GetValue(valueName) is not byte[] { Length: > 0 } bytes || (bytes[0] & 1) == 0;
    }

    /// <summary>Flips the StartupApproved flag the same way Task Manager does; the app's own entry is never touched.</summary>
    public static void SetStartupEnabled(string approvedKey, bool enabled)
    {
        var parts = approvedKey.Split('|');
        if (parts.Length != 3) throw new ArgumentException("Bad startup key.");
        var (hiveName, subKey, value) = (parts[0], parts[1], parts[2]);
        var bytes = new byte[12];
        bytes[0] = (byte)(enabled ? 0x02 : 0x03);
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);

        var hive = hiveName == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        try
        {
            using var key = hive.CreateSubKey(subKey, true);
            key.SetValue(value, bytes, RegistryValueKind.Binary);
        }
        catch (Exception ex) when (hiveName == "HKLM" && ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            var hex = Convert.ToHexString(bytes);
            Elevated.Run("reg.exe", $"add \"HKLM\\{subKey}\" /v \"{value}\" /t REG_BINARY /d {hex} /f");
        }
    }

    // ── services ───────────────────────────────────────────────

    public static List<ServiceItem> GetServices()
    {
        var list = new List<ServiceItem>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root is null) return list;
        var scm = OpenSCManager(null, null, ScManagerConnect);
        try
        {
            foreach (var name in root.GetSubKeyNames())
            {
                RegistryKey? key;
                try { key = root.OpenSubKey(name); }
                catch (System.Security.SecurityException) { continue; } // a few services hide their key from non-admins
                using var _ = key;
                if (key is null) continue;
                if (key.GetValue("Type") is not int type || (type & 0x30) == 0) continue; // not a Win32 service (drivers etc.)
                if (key.GetValue("Start") is not int start) continue;
                var image = key.GetValue("ImagePath")?.ToString() ?? "";
                if (image.Length == 0) continue;

                var binary = ExePathOf(image);
                if (Path.GetFileName(binary).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var p = key.OpenSubKey("Parameters");
                        if (p?.GetValue("ServiceDll")?.ToString() is { Length: > 0 } dll) binary = Expand(dll);
                    }
                    catch (System.Security.SecurityException) { } // hidden from non-admins; svchost is enough to go on
                }
                var company = CompanyOf(binary);
                var display = Indirect(key.GetValue("DisplayName")?.ToString()) ?? name;

                list.Add(new ServiceItem
                {
                    Name = name,
                    DisplayName = display,
                    StartMode = ModeOf(start, key.GetValue("DelayedAutostart") is int d && d == 1),
                    Status = StatusOf(scm, name),
                    Publisher = company,
                    Path = image,
                    IsMicrosoft = company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
                                  || (company.Length == 0 && image.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
                });
            }
        }
        finally
        {
            if (scm != 0) CloseServiceHandle(scm);
        }
        return list.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ModeOf(int start, bool delayed) => start switch
    {
        2 => delayed ? ServiceModes.Delayed : ServiceModes.Automatic,
        3 => ServiceModes.Manual,
        4 => ServiceModes.Disabled,
        _ => "Boot"
    };

    /// <summary>Changes the start type with sc.exe, which needs administrator rights.</summary>
    public static void SetServiceMode(string name, string mode)
    {
        var arg = mode switch
        {
            ServiceModes.Automatic => "auto",
            ServiceModes.Delayed => "delayed-auto",
            ServiceModes.Manual => "demand",
            ServiceModes.Disabled => "disabled",
            _ => throw new ArgumentException($"Unknown start type {mode}.")
        };
        Elevated.Run("sc.exe", $"config \"{name}\" start= {arg}");
    }

    private static string StatusOf(nint scm, string name)
    {
        if (scm == 0) return "";
        var h = OpenService(scm, name, ServiceQueryStatus);
        if (h == 0) return "";
        try
        {
            if (!QueryServiceStatus(h, out var st)) return "";
            return st.dwCurrentState switch
            {
                1 => "Stopped",
                2 => "Starting",
                3 => "Stopping",
                4 => "Running",
                7 => "Paused",
                _ => "Pending"
            };
        }
        finally { CloseServiceHandle(h); }
    }

    // ── scheduled tasks ────────────────────────────────────────

    /// <summary>Tasks that run at startup or at logon, read through the Task Scheduler COM API.</summary>
    public static List<TaskItem> GetTasks()
    {
        var list = new List<TaskItem>();
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is not available.");
        dynamic svc = Activator.CreateInstance(type)!;
        try
        {
            svc.Connect();
            Walk(svc.GetFolder("\\"), list);
        }
        finally
        {
            Marshal.FinalReleaseComObject(svc);
        }
        return list.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(dynamic folder, List<TaskItem> list)
    {
        dynamic tasks = folder.GetTasks(1); // include hidden
        for (var i = 1; i <= (int)tasks.Count; i++)
        {
            try
            {
                dynamic task = tasks.Item(i);
                dynamic def = task.Definition;
                var triggers = new List<string>();
                dynamic trigs = def.Triggers;
                for (var j = 1; j <= (int)trigs.Count; j++)
                {
                    var t = (int)trigs.Item(j).Type;
                    if (t == 8) triggers.Add("At startup");
                    else if (t == 9) triggers.Add("At log on");
                }
                if (triggers.Count == 0) continue;

                var actions = new List<string>();
                dynamic acts = def.Actions;
                for (var j = 1; j <= (int)acts.Count; j++)
                {
                    dynamic a = acts.Item(j);
                    actions.Add((int)a.Type == 0 ? $"{a.Path} {a.Arguments}".Trim() : "(COM handler)");
                }

                string path = task.Path;
                list.Add(new TaskItem
                {
                    Path = path,
                    Name = task.Name,
                    Folder = Path.GetDirectoryName(path)?.Replace('/', '\\') ?? "\\",
                    Trigger = string.Join(", ", triggers.Distinct()),
                    Action = string.Join("; ", actions),
                    Author = (string?)def.RegistrationInfo.Author ?? "",
                    Enabled = task.Enabled
                });
            }
            catch
            {
                // a task we may not read; skip it
            }
        }

        dynamic folders = folder.GetFolders(0);
        for (var i = 1; i <= (int)folders.Count; i++)
        {
            try { Walk(folders.Item(i), list); } catch { }
        }
    }

    public static void SetTaskEnabled(string path, bool enabled)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")!;
            dynamic svc = Activator.CreateInstance(type)!;
            svc.Connect();
            var folder = Path.GetDirectoryName(path) is { Length: > 0 } f ? f : "\\";
            dynamic task = svc.GetFolder(folder).GetTask(Path.GetFileName(path));
            task.Enabled = enabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
        {
            Elevated.Run("schtasks.exe", $"/Change /TN \"{path}\" {(enabled ? "/ENABLE" : "/DISABLE")}");
        }
    }

    // ── change log ─────────────────────────────────────────────

    public static List<ChangeEntry> LoadChanges()
    {
        try
        {
            return File.Exists(ChangesPath)
                ? JsonSerializer.Deserialize<List<ChangeEntry>>(File.ReadAllText(ChangesPath)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Record(ChangeEntry entry)
    {
        var all = LoadChanges();
        all.Insert(0, entry);
        SaveChanges(all);
    }

    private static void SaveChanges(List<ChangeEntry> all)
    {
        AppPaths.EnsureRoot();
        File.WriteAllText(ChangesPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Undo(ChangeEntry entry)
    {
        switch (entry.Kind)
        {
            case "Startup app": SetStartupEnabled(entry.Key, entry.Before == "On"); break;
            case "Service": SetServiceMode(entry.Key, entry.Before); break;
            case "Scheduled task": SetTaskEnabled(entry.Key, entry.Before == "On"); break;
            default: throw new InvalidOperationException($"Cannot undo a {entry.Kind} change.");
        }
        var all = LoadChanges();
        foreach (var e in all.Where(e => e.When == entry.When && e.Key == entry.Key)) e.Undone = true;
        SaveChanges(all);
    }

    // ── helpers ────────────────────────────────────────────────

    /// <summary>The program a command line starts: the quoted part, or everything up to ".exe".</summary>
    public static string ExePathOf(string command)
    {
        var c = command.Trim();
        if (c.Length == 0) return "";
        string path;
        if (c[0] == '"')
        {
            var end = c.IndexOf('"', 1);
            path = end > 0 ? c[1..end] : c.Trim('"');
        }
        else
        {
            var i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = i > 0 ? c[..(i + 4)] : c.Split(' ')[0];
        }
        return Expand(path);
    }

    private static string Expand(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(win, path[12..]);
        else if (path.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(win, path);
        else if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path[4..];
        return path;
    }

    public static string CompanyOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        lock (Companies)
        {
            if (Companies.TryGetValue(path, out var c)) return c;
            c = "";
            try
            {
                if (File.Exists(path)) c = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "";
            }
            catch { }
            Companies[path] = c;
            return c;
        }
    }

    private static (string Target, string Args) ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return (lnk, "");
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            string args = sc.Arguments;
            return string.IsNullOrEmpty(target) ? (lnk, "") : (target, args);
        }
        catch { return (lnk, ""); }
    }

    private static string? Indirect(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '@') return value;
        var sb = new StringBuilder(512);
        if (SHLoadIndirectString(value, sb, sb.Capacity, 0) == 0) return sb.ToString();
        // Driver-package names look like "@oem12.inf,%Key%;Fallback name"; the part after ';' is readable.
        var semi = value.LastIndexOf(';');
        return semi >= 0 && semi < value.Length - 1 ? value[(semi + 1)..] : value;
    }

    // ── native ─────────────────────────────────────────────────

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern nint OpenSCManager(string? machine, string? db, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern nint OpenService(nint scm, string name, uint access);
    [DllImport("advapi32.dll")] private static extern bool QueryServiceStatus(nint h, out SERVICE_STATUS status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint h);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder buf, int size, nint reserved);
}

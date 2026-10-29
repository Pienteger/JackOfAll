using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JackOfAll.UserControls.Services;

/// <summary>Win32 calls the clipboard history needs: change notifications, the owning app, the global shortcut and synthetic Ctrl+V.</summary>
internal static class ClipboardNative
{
    public const int WmClipboardUpdate = 0x031D;
    public const int WmHotkey = 0x0312;
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    public const uint VkV = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardOwner();

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public int Type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }

    /// <summary>Work area (in physical pixels) of the monitor under the mouse.</summary>
    public static Rect WorkAreaAtCursor()
    {
        GetCursorPos(out var point);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        return info.Work;
    }

    /// <summary>
    /// Sends Ctrl+V to whatever has focus. Shift, Alt and Win still held from the picker's own shortcut are released
    /// first, so Shift+Enter in the picker doesn't arrive as Ctrl+Shift+V (which would open the picker again).
    /// </summary>
    public static void SendPaste()
    {
        var keys = new List<Input>();
        foreach (var vk in new[] { 0x10, 0x12, 0x5B, 0x5C })
            if (GetAsyncKeyState(vk) < 0) keys.Add(Key(vk, up: true));
        keys.Add(Key(0x11, up: false));
        keys.Add(Key((int)VkV, up: false));
        keys.Add(Key((int)VkV, up: true));
        keys.Add(Key(0x11, up: true));
        SendInput((uint)keys.Count, [.. keys], Marshal.SizeOf<Input>());
    }

    private static Input Key(int vk, bool up)
    {
        var input = new Input { Type = 1 };
        input.U.Keyboard.Vk = (ushort)vk;
        input.U.Keyboard.Flags = up ? 2u : 0u;
        return input;
    }

    private static readonly Dictionary<uint, string> AppNames = [];

    /// <summary>The app that put data on the clipboard ("Google Chrome", "Windows Explorer"), or "" when unknown.</summary>
    public static string ClipboardSourceApp(out string processName)
    {
        processName = "";
        var hwnd = GetClipboardOwner();
        if (hwnd == nint.Zero) hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName.ToLowerInvariant();
            if (AppNames.TryGetValue(pid, out var cached)) return cached;
            var name = process.ProcessName;
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
            catch { /* elevated or protected process: keep the process name */ }
            if (AppNames.Count > 300) AppNames.Clear();
            AppNames[pid] = name;
            return name;
        }
        catch
        {
            return "";
        }
    }
}

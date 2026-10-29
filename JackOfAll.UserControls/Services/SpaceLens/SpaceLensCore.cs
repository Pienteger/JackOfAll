// Copied from the standalone SpaceLens app (SpaceLens.Core.cs). Keep the two in sync; it is C# 5 so it still builds there.
#nullable disable
// SpaceLens core: scanning, treemap layout, Recycle Bin, and the Crow cleaners (build output + system junk).
// No UI code here. Written in C# 5 so it compiles with the .NET Framework compiler that ships with Windows
// (standalone SpaceLens) and with modern .NET (the JackOfAll copy).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace JackOfAll.UserControls.Services.SpaceLens
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom, pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        public const uint DIRECTORY = 0x10, REPARSE = 0x400, READONLY = 0x1, NORMAL = 0x80;
        public static readonly IntPtr Invalid = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr h, out WIN32_FIND_DATA data);
        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFileW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool RemoveDirectoryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetFileAttributesW(string path, uint attributes);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFileAttributesW(string path);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

        /// <summary>"C:\x" becomes "\\?\C:\x" so paths over 260 characters still work.</summary>
        public static string Long(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            return path.StartsWith(@"\\") ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        public static DateTime Utc(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            long t = ((long)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
            try { return DateTime.FromFileTimeUtc(t); } catch { return DateTime.MinValue; }
        }

        public static long Size(WIN32_FIND_DATA fd) { return ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow; }

        /// <summary>Lists one directory. FindExInfoBasic + large fetch: no 8.3 names, big batches.</summary>
        public static bool List(string dir, Action<WIN32_FIND_DATA> each)
        {
            string p = dir.EndsWith("\\") ? dir : dir + "\\";
            WIN32_FIND_DATA fd;
            IntPtr h = FindFirstFileExW(Long(p) + "*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Invalid) return false;
            try
            {
                do
                {
                    if (fd.cFileName == "." || fd.cFileName == "..") continue;
                    each(fd);
                } while (FindNextFileW(h, out fd));
            }
            finally { FindClose(h); }
            return true;
        }
    }

    static class Fmt
    {
        static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };
        public static string Size(long b)
        {
            double v = b; int i = 0;
            while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
            if (i == 0) return b + " B";
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + Units[i];
        }
        public static string N(long n) { return n.ToString("N0"); }
        public static string Pct(long part, long whole) { return whole > 0 ? (part * 100.0 / whole).ToString("0.0") + "%" : "-"; }
    }

    sealed class Node
    {
        public string Name, Ext;
        public Node Parent;
        public List<Node> Children;
        public long Size;
        public int Files;
        public byte Cat;
        public bool IsDir;

        public string FullPath
        {
            get
            {
                if (Parent == null) return Name;
                string p = Parent.FullPath;
                return p.EndsWith("\\") ? p + Name : p + "\\" + Name;
            }
        }

        public bool IsUnder(Node ancestor)
        {
            for (Node x = this; x != null; x = x.Parent) if (x == ancestor) return true;
            return false;
        }

        static int BySizeDesc(Node a, Node b) { return b.Size.CompareTo(a.Size); }

        /// <summary>Takes this node out of the tree and fixes the sizes and sort order of every ancestor.</summary>
        public void Detach()
        {
            Node p = Parent;
            if (p == null) return;
            p.Children.Remove(this);
            int files = IsDir ? Files : 1;
            for (Node x = p; x != null; x = x.Parent)
            {
                x.Size -= Size; x.Files -= files;
                if (x.Parent != null) x.Parent.Children.Sort(BySizeDesc);
            }
        }
    }

    static class Cats
    {
        public const int Count = 8, Other = 7;
        public static readonly string[] Names = { "Video", "Images", "Audio", "Archives & disk images", "Apps & system files", "Code & dev", "Documents", "Other" };
        public static readonly Color[] Colors =
        {
            Color.FromArgb(236, 104, 88), Color.FromArgb(245, 180, 70), Color.FromArgb(126, 202, 112), Color.FromArgb(86, 162, 232),
            Color.FromArgb(166, 136, 232), Color.FromArgb(74, 202, 196), Color.FromArgb(238, 132, 186), Color.FromArgb(150, 156, 168)
        };
        static readonly Dictionary<string, byte> map = new Dictionary<string, byte>();
        static readonly ConcurrentDictionary<string, string> pool = new ConcurrentDictionary<string, string>();

        static Cats()
        {
            Add(0, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg ts m2ts mts 3gp vob ogv rm rmvb");
            Add(1, "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 cr3 nef arw dng psd ico svg ai xcf avif jxl");
            Add(2, "mp3 wav flac aac ogg m4a wma opus aiff aif mid midi ape");
            Add(3, "zip rar 7z tar gz tgz bz2 xz zst iso img vhd vhdx vmdk vdi wim esd cab dmg qcow2 pak");
            Add(4, "exe dll sys msi msix msp appx appxbundle ocx drv cpl scr mui etl evtx edb dat regtrans-ms blf");
            Add(5, "cs js mjs cjs ts jsx tsx py java c cpp cc h hpp go rs rb php swift kt json xml yml yaml toml ini cfg sql html css scss map pdb obj lib o a class jar pyc whl nupkg ipynb sh ps1 bat cmd lock so dylib node wasm");
            Add(6, "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf md csv epub mobi pages numbers key one msg eml pst ost");
        }

        static void Add(byte cat, string list) { foreach (string e in list.Split(' ')) map["." + e] = cat; }

        public static void Classify(string name, out string ext, out byte cat)
        {
            int i = name.LastIndexOf('.');
            if (i <= 0 || i == name.Length - 1 || name.Length - i > 16) { ext = ""; cat = Other; return; }
            string e = name.Substring(i).ToLowerInvariant();
            ext = pool.GetOrAdd(e, e);
            byte c;
            cat = map.TryGetValue(ext, out c) ? c : (byte)Other;
        }
    }

    sealed class Scanner
    {
        public volatile bool Cancel;
        public volatile string Current = "";
        public long Files, Dirs, Bytes, Denied;

        public Node Run(string path)
        {
            Node root = new Node { Name = path, IsDir = true };
            Scan(root, path, 0);
            return root;
        }

        void Scan(Node d, string path, int depth)
        {
            List<Node> kids = new List<Node>(), subs = new List<Node>();
            d.Children = kids;
            if (Cancel) return;
            Current = path;
            string p = path.EndsWith("\\") ? path : path + "\\";

            Native.WIN32_FIND_DATA fd;
            IntPtr h = Native.FindFirstFileExW(Native.Long(p) + "*", 1, out fd, 0, IntPtr.Zero, 2);
            if (h == Native.Invalid) { Interlocked.Increment(ref Denied); return; }
            try
            {
                do
                {
                    string n = fd.cFileName;
                    if (n == "." || n == "..") continue;
                    if ((fd.dwFileAttributes & Native.DIRECTORY) != 0)
                    {
                        if ((fd.dwFileAttributes & Native.REPARSE) != 0) continue; // junctions/symlinks: never double count
                        Node c = new Node { Name = n, IsDir = true, Parent = d };
                        kids.Add(c); subs.Add(c);
                        Interlocked.Increment(ref Dirs);
                    }
                    else
                    {
                        long sz = Native.Size(fd);
                        Node f = new Node { Name = n, Parent = d, Size = sz };
                        Cats.Classify(n, out f.Ext, out f.Cat);
                        kids.Add(f);
                        d.Size += sz; d.Files++;
                        Interlocked.Increment(ref Files);
                        Interlocked.Add(ref Bytes, sz);
                    }
                } while (!Cancel && Native.FindNextFileW(h, out fd));
            }
            finally { Native.FindClose(h); }

            if (depth < 3 && subs.Count > 1)
                Parallel.ForEach(subs, c => Scan(c, p + c.Name, depth + 1));
            else
                foreach (Node c in subs) Scan(c, p + c.Name, depth + 1);

            foreach (Node c in subs) { d.Size += c.Size; d.Files += c.Files; }
            kids.Sort((a, b) => b.Size.CompareTo(a.Size));
            kids.TrimExcess();
        }
    }

    static class Treemap
    {
        /// <summary>
        /// Squarified treemap (Bruls, Huizing, van Wijk). Expects kids sorted by size, descending;
        /// stops early once the remaining items would be smaller than a pixel.
        /// </summary>
        public static void Squarify(List<Node> kids, RectangleF r, Action<Node, RectangleF> place)
        {
            int n = 0; double total = 0;
            while (n < kids.Count && kids[n].Size > 0) { total += kids[n].Size; n++; }
            if (n == 0 || r.Width <= 0 || r.Height <= 0) return;
            double scale = (double)r.Width * r.Height / total;
            double x = r.X, y = r.Y, w = r.Width, h = r.Height;
            int i = 0;
            while (i < n)
            {
                if (w < 0.5 || h < 0.5) return;
                if (kids[i].Size * scale < 0.25) return;
                double side = Math.Min(w, h), side2 = side * side;
                double first = kids[i].Size * scale, sum = 0, worst = double.MaxValue;
                int j = i;
                while (j < n)
                {
                    double a = kids[j].Size * scale, sm = sum + a, s2 = sm * sm;
                    double nw = Math.Max(side2 * first / s2, s2 / (side2 * a));
                    if (j > i && nw > worst) break;
                    sum = sm; worst = nw; j++;
                }
                if (w >= h)
                {
                    double cw = Math.Min(w, sum / h), yy = y;
                    for (int k = i; k < j; k++)
                    {
                        double kh = kids[k].Size * scale / cw;
                        place(kids[k], new RectangleF((float)x, (float)yy, (float)cw, (float)kh));
                        yy += kh;
                    }
                    x += cw; w -= cw;
                }
                else
                {
                    double rh = Math.Min(h, sum / w), xx = x;
                    for (int k = i; k < j; k++)
                    {
                        double kw = kids[k].Size * scale / rh;
                        place(kids[k], new RectangleF((float)xx, (float)y, (float)kw, (float)rh));
                        xx += kw;
                    }
                    y += rh; h -= rh;
                }
                i = j;
            }
        }
    }

    static class Recycler
    {
        /// <summary>Moves a file or folder to the Recycle Bin. Windows shows its own progress for big folders.</summary>
        public static bool Send(string path, IntPtr owner, out string error)
        {
            error = null;
            Native.SHFILEOPSTRUCT op = new Native.SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = 3,                     // FO_DELETE
                pFrom = path + "\0\0",
                fFlags = 0x40 | 0x10 | 0x400  // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
            };
            int rc = Native.SHFileOperationW(ref op);
            if (op.fAnyOperationsAborted != 0) { error = "Cancelled."; return false; }
            if (rc != 0) { error = "Windows could not move it to the Recycle Bin (code 0x" + rc.ToString("X") + "). It may be in use."; return false; }
            return !File.Exists(path) && !Directory.Exists(path);
        }
    }

    // ------------------------------------------------------------------ Crow: find and remove regenerable junk

    sealed class CleanResult
    {
        public long Freed;
        public int Deleted, Skipped;
        public void Add(CleanResult o) { Freed += o.Freed; Deleted += o.Deleted; Skipped += o.Skipped; }
    }

    /// <summary>A project type: the marker file that identifies its root and the regenerable folders next to it.</summary>
    sealed class DevPreset
    {
        public readonly string Name, LookFor;
        public readonly string[] Remove;
        public DevPreset(string name, string lookFor, params string[] remove) { Name = name; LookFor = lookFor; Remove = remove; }

        public bool Marks(string fileName)
        {
            return LookFor.StartsWith("*")
                ? fileName.EndsWith(LookFor.Substring(1), StringComparison.OrdinalIgnoreCase)
                : string.Equals(fileName, LookFor, StringComparison.OrdinalIgnoreCase);
        }

        static readonly string[] PythonCaches = { "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache", ".tox" };

        public static readonly DevPreset[] All =
        {
            new DevPreset(".NET", "*.csproj", "bin", "obj"),
            new DevPreset(".NET", "*.fsproj", "bin", "obj"),
            new DevPreset(".NET", "*.vbproj", "bin", "obj"),
            new DevPreset("Node.js", "package.json", "node_modules", ".next", ".nuxt", ".turbo", ".parcel-cache", ".svelte-kit", ".angular"),
            new DevPreset("Rust", "Cargo.toml", "target"),
            new DevPreset("Maven", "pom.xml", "target"),
            new DevPreset("Gradle", "build.gradle", "build", ".gradle"),
            new DevPreset("Gradle", "build.gradle.kts", "build", ".gradle"),
            new DevPreset("Python", "pyproject.toml", PythonCaches),
            new DevPreset("Python", "requirements.txt", PythonCaches),
            new DevPreset("Flutter", "pubspec.yaml", "build", ".dart_tool"),
            new DevPreset("CMake", "CMakeLists.txt", "cmake-build-debug", "cmake-build-release"),
        };
    }

    /// <summary>One thing Crow can clean: a build-output folder found in the scanned tree, or a system junk location.</summary>
    sealed class JunkItem
    {
        public bool IsBuild;
        public string Name, Kind, Path;   // Kind: project type ("Node.js") or junk category ("Browsers")
        public Node Node;                 // build output: the folder in the scanned tree
        public SystemJunkLocation Location;
        public long Size;
        public int Files;
        public bool Checked, RequiresAdmin;
    }

    static class BuildJunk
    {
        static readonly HashSet<string> NeverDescendInto = new HashSet<string>(new[] { "node_modules", ".git", ".hg", ".svn" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Finds build output in an already scanned tree, so it costs no disk access: a folder counts
        /// when it sits next to its project file (bin/obj next to *.csproj, node_modules next to package.json...).
        /// Keeps descending below project roots, so nested projects in a monorepo are found too.
        /// </summary>
        public static List<JunkItem> Find(Node root)
        {
            List<JunkItem> found = new List<JunkItem>();
            Stack<Node> stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Node d = stack.Pop();
                if (d.Children == null) continue;
                HashSet<string> junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DevPreset preset in DevPreset.All)
                {
                    bool marked = false;
                    foreach (Node f in d.Children) if (!f.IsDir && preset.Marks(f.Name)) { marked = true; break; }
                    if (!marked) continue;
                    foreach (Node c in d.Children)
                    {
                        if (!c.IsDir || c.Size == 0 || junk.Contains(c.Name)) continue;
                        foreach (string candidate in preset.Remove)
                        {
                            if (!string.Equals(candidate, c.Name, StringComparison.OrdinalIgnoreCase)) continue;
                            junk.Add(c.Name);
                            found.Add(new JunkItem { IsBuild = true, Name = c.Name, Kind = preset.Name, Path = c.FullPath, Node = c, Size = c.Size, Files = c.Files, Checked = true });
                            break;
                        }
                    }
                }
                foreach (Node c in d.Children)
                    if (c.IsDir && !junk.Contains(c.Name) && !NeverDescendInto.Contains(c.Name)) stack.Push(c);
            }
            found.Sort((a, b) => b.Size.CompareTo(a.Size));
            return found;
        }
    }

    /// <summary>A well-known place where Windows or apps leave disposable files. Only its contents are cleaned.</summary>
    sealed class SystemJunkLocation
    {
        public string Name, Category;
        public List<string> Paths;
        public double MinAgeHours;      // files changed more recently are left alone
        public bool SelectedByDefault, RequiresAdmin;
    }

    static class SystemJunk
    {
        const string SYSTEM = "System", BROWSERS = "Browsers", APPS = "Apps", DEVELOPER = "Developer";
        static readonly string[] ChromiumCacheFolders = { "Cache", "Code Cache", "GPUCache" };
        static readonly string[] ElectronCacheFolders = { "Cache", "Code Cache", "GPUCache", "CachedData" };

        static SystemJunkLocation L(string name, string cat, IEnumerable<string> paths, double hours, bool def, bool admin)
        {
            return new SystemJunkLocation { Name = name, Category = cat, Paths = new List<string>(paths), MinAgeHours = hours, SelectedByDefault = def, RequiresAdmin = admin };
        }

        /// <summary>The junk locations that exist on this machine (Crow's catalog).</summary>
        public static List<SystemJunkLocation> GetLocations()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Func<string[], string> P = parts => System.IO.Path.Combine(parts);

            List<SystemJunkLocation> all = new List<SystemJunkLocation>
            {
                L("User temp files", SYSTEM, new[] { System.IO.Path.GetTempPath() }, 24, true, false),
                L("Windows temp files", SYSTEM, new[] { P(new[] { windows, "Temp" }) }, 24, true, true),
                L("Crash dumps", SYSTEM, new[] { P(new[] { local, "CrashDumps" }) }, 0, true, false),
                L("Windows error reports", SYSTEM, new[] { P(new[] { local, "Microsoft", "Windows", "WER" }), P(new[] { programData, "Microsoft", "Windows", "WER" }) }, 0, true, false),
                L("DirectX shader cache", SYSTEM, new[] { P(new[] { local, "D3DSCache" }) }, 0, true, false),
                L("Windows Update downloads", SYSTEM, new[] { P(new[] { windows, "SoftwareDistribution", "Download" }) }, 24, false, true),
                L("Internet cache", BROWSERS, new[] { P(new[] { local, "Microsoft", "Windows", "INetCache" }) }, 0, true, false),
                L("Chrome cache", BROWSERS, ChromiumCaches(P(new[] { local, "Google", "Chrome", "User Data" })), 0, true, false),
                L("Edge cache", BROWSERS, ChromiumCaches(P(new[] { local, "Microsoft", "Edge", "User Data" })), 0, true, false),
                L("Brave cache", BROWSERS, ChromiumCaches(P(new[] { local, "BraveSoftware", "Brave-Browser", "User Data" })), 0, true, false),
                L("Firefox cache", BROWSERS, SubFolders(P(new[] { local, "Mozilla", "Firefox", "Profiles" }), "cache2"), 0, true, false),
                L("App caches (VS Code, Discord, Slack, Teams)", APPS, ElectronCaches(roaming, new[] { "Code", "discord", "Slack", P(new[] { "Microsoft", "Teams" }) }), 0, true, false),
                L("NVIDIA shader cache", APPS, new[] { P(new[] { local, "NVIDIA", "DXCache" }), P(new[] { local, "NVIDIA", "GLCache" }) }, 0, false, false),
                L("npm cache", DEVELOPER, new[] { P(new[] { local, "npm-cache" }) }, 0, false, false),
                L("Yarn cache", DEVELOPER, new[] { P(new[] { local, "Yarn", "Cache" }) }, 0, false, false),
                L("pip cache", DEVELOPER, new[] { P(new[] { local, "pip", "cache" }) }, 0, false, false),
                L("NuGet HTTP cache", DEVELOPER, new[] { P(new[] { local, "NuGet", "v3-cache" }), P(new[] { local, "NuGet", "plugins-cache" }) }, 0, false, false),
                L("NuGet packages", DEVELOPER, new[] { P(new[] { home, ".nuget", "packages" }) }, 0, false, false),
                L("Gradle caches", DEVELOPER, new[] { P(new[] { home, ".gradle", "caches" }) }, 0, false, false),
            };

            List<SystemJunkLocation> existing = new List<SystemJunkLocation>();
            foreach (SystemJunkLocation l in all)
            {
                List<string> paths = new List<string>();
                foreach (string p in l.Paths)
                {
                    bool exists; try { exists = Directory.Exists(p); } catch { exists = false; }
                    if (exists && !paths.Exists(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) paths.Add(p);
                }
                if (paths.Count > 0) { l.Paths = paths; existing.Add(l); }
            }
            return existing;
        }

        static List<string> ChromiumCaches(string userData)
        {
            List<string> r = new List<string>();
            try
            {
                if (!Directory.Exists(userData)) return r;
                foreach (string profile in Directory.EnumerateDirectories(userData))
                {
                    string name = System.IO.Path.GetFileName(profile);
                    if (name != "Default" && !name.StartsWith("Profile ") && name != "Guest Profile") continue;
                    foreach (string cache in ChromiumCacheFolders) r.Add(System.IO.Path.Combine(profile, cache));
                }
                r.Add(System.IO.Path.Combine(userData, "ShaderCache"));
                r.Add(System.IO.Path.Combine(userData, "GrShaderCache"));
            }
            catch { }
            return r;
        }

        static List<string> ElectronCaches(string root, string[] apps)
        {
            List<string> r = new List<string>();
            foreach (string app in apps) foreach (string cache in ElectronCacheFolders) r.Add(System.IO.Path.Combine(root, app, cache));
            return r;
        }

        static List<string> SubFolders(string root, string child)
        {
            List<string> r = new List<string>();
            try { if (Directory.Exists(root)) foreach (string p in Directory.EnumerateDirectories(root)) r.Add(System.IO.Path.Combine(p, child)); }
            catch { }
            return r;
        }

        /// <summary>Measures every location in parallel: the files older than its cutoff, and how big they are.</summary>
        public static List<JunkItem> Measure(List<SystemJunkLocation> locations)
        {
            DateTime now = DateTime.UtcNow;
            JunkItem[] items = new JunkItem[locations.Count];
            Parallel.For(0, locations.Count, i =>
            {
                SystemJunkLocation l = locations[i];
                DateTime cutoff = now.AddHours(-l.MinAgeHours);
                long size = 0, files = 0;
                foreach (string root in l.Paths) Walk(root, cutoff, ref size, ref files, 0);
                items[i] = new JunkItem
                {
                    Name = l.Name, Kind = l.Category, Path = string.Join("; ", l.Paths.ToArray()), Location = l,
                    Size = size, Files = (int)files, Checked = l.SelectedByDefault, RequiresAdmin = l.RequiresAdmin
                };
            });
            List<JunkItem> list = new List<JunkItem>();
            foreach (JunkItem it in items) if (it.Files > 0) list.Add(it);
            list.Sort((a, b) => b.Size.CompareTo(a.Size));
            return list;
        }

        static void Walk(string dir, DateTime cutoff, ref long size, ref long files, int depth)
        {
            List<string> subs = new List<string>();
            long s = 0, f = 0;
            Native.List(dir, fd =>
            {
                if ((fd.dwFileAttributes & Native.DIRECTORY) != 0)
                {
                    if ((fd.dwFileAttributes & Native.REPARSE) == 0) subs.Add(System.IO.Path.Combine(dir, fd.cFileName));
                }
                else if (Native.Utc(fd.ftLastWriteTime) < cutoff) { s += Native.Size(fd); f++; }
            });
            size += s; files += f;
            foreach (string sub in subs) Walk(sub, cutoff, ref size, ref files, depth + 1);
        }

        /// <summary>
        /// Deletes the files Measure counted, then removes sub folders that are old and now empty.
        /// Files that are locked or need elevation are skipped.
        /// </summary>
        public static CleanResult Clean(SystemJunkLocation l)
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-l.MinAgeHours);
            CleanResult r = new CleanResult();
            List<string> oldDirs = new List<string>();
            foreach (string root in l.Paths) CleanDir(root, cutoff, r, oldDirs);
            oldDirs.Sort((a, b) => b.Length.CompareTo(a.Length)); // deepest first
            foreach (string d in oldDirs) Native.RemoveDirectoryW(Native.Long(d)); // fails harmlessly when not empty
            return r;
        }

        static void CleanDir(string dir, DateTime cutoff, CleanResult r, List<string> oldDirs)
        {
            List<string> subs = new List<string>();
            List<KeyValuePair<string, long>> files = new List<KeyValuePair<string, long>>();
            Native.List(dir, fd =>
            {
                string path = System.IO.Path.Combine(dir, fd.cFileName);
                if ((fd.dwFileAttributes & Native.DIRECTORY) != 0)
                {
                    if ((fd.dwFileAttributes & Native.REPARSE) != 0) return;
                    // Creation time, because deleting files bumps a folder's write time.
                    if (Native.Utc(fd.ftCreationTime) < cutoff) oldDirs.Add(path);
                    subs.Add(path);
                }
                else if (Native.Utc(fd.ftLastWriteTime) < cutoff) files.Add(new KeyValuePair<string, long>(path, Native.Size(fd)));
            });
            foreach (KeyValuePair<string, long> f in files)
            {
                if (Deleter.TryDeleteFile(f.Key)) { r.Freed += f.Value; r.Deleted++; } else r.Skipped++;
            }
            foreach (string s in subs) CleanDir(s, cutoff, r, oldDirs);
        }
    }

    static class Deleter
    {
        /// <summary>
        /// Deletes a folder tree permanently. Keeps going past failures, clears read-only flags,
        /// retries briefly on transient locks, and removes links (junctions, symlinks) without touching their targets.
        /// </summary>
        public static CleanResult DeleteTree(string root)
        {
            CleanResult total = new CleanResult();
            object gate = new object();
            Delete(root, 0, total, gate);
            return total;
        }

        static void Delete(string dir, int depth, CleanResult total, object gate)
        {
            List<string> subs = new List<string>();
            CleanResult r = new CleanResult();
            List<Native.WIN32_FIND_DATA> entries = new List<Native.WIN32_FIND_DATA>();
            Native.List(dir, entries.Add);
            foreach (Native.WIN32_FIND_DATA fd in entries)
            {
                string path = System.IO.Path.Combine(dir, fd.cFileName);
                bool isDir = (fd.dwFileAttributes & Native.DIRECTORY) != 0, isLink = (fd.dwFileAttributes & Native.REPARSE) != 0;
                if (isDir && !isLink) { subs.Add(path); continue; }
                if (TryDelete(path, isDir)) { r.Deleted++; if (!isLink) r.Freed += Native.Size(fd); }
                else r.Skipped++;
            }
            lock (gate) total.Add(r);

            if (depth < 2 && subs.Count > 1) Parallel.ForEach(subs, s => Delete(s, depth + 1, total, gate));
            else foreach (string s in subs) Delete(s, depth + 1, total, gate);

            if (!TryDelete(dir, true)) lock (gate) total.Skipped++;
        }

        public static bool TryDeleteFile(string path) { return TryDelete(path, false); }

        static bool TryDelete(string path, bool isDir)
        {
            string lp = Native.Long(path);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // A link's own kind may not match its target, so try the other kind too.
                if (isDir ? (Native.RemoveDirectoryW(lp) || Native.DeleteFileW(lp)) : (Native.DeleteFileW(lp) || Native.RemoveDirectoryW(lp))) return true;
                uint a = Native.GetFileAttributesW(lp);
                if (a == 0xFFFFFFFF) return true; // already gone
                if ((a & Native.READONLY) != 0) { Native.SetFileAttributesW(lp, a & ~Native.READONLY); continue; }
                if (attempt < 2) Thread.Sleep(25 << attempt); // antivirus or indexer holding it for a moment
            }
            return false;
        }
    }
}

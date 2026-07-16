using System.IO;

namespace JackOfAll.Lib;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JackOfAll");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}

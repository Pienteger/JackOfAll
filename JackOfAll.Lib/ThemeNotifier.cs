namespace JackOfAll.Lib;

public static class ThemeNotifier
{
    public static event Action? ThemeChanged;

    public static void RaiseThemeChanged() => ThemeChanged?.Invoke();
}

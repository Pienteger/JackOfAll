using System.Windows;
using System.Windows.Media;
using JackOfAll.Lib.Settings;
using Microsoft.Win32;

namespace JackOfAll.AppUi.Themes;

public static class ThemeManager
{
    public static void Apply(AppSettings settings)
    {
        var isDark = settings.ThemeMode switch
        {
            Lib.Settings.ThemeMode.Dark => true,
            Lib.Settings.ThemeMode.Light => false,
            Lib.Settings.ThemeMode.System => IsSystemDark(),
            _ => true
        };

        var res = Application.Current.Resources;

        if (isDark)
        {
            res["AppBackground"] = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
            res["SurfaceBackground"] = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x30));
            res["SurfaceBorder"] = new SolidColorBrush(Color.FromRgb(0x3e, 0x3e, 0x42));
            res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc));
            res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            res["TextDimmed"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x7a, 0xcc));
            res["TabBarBackground"] = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x30));
            res["TabActiveBackground"] = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
            res["TabHoverBackground"] = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
            res["GridAltRow"] = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
            res["ErrorBackground"] = new SolidColorBrush(Color.FromRgb(0x3a, 0x1d, 0x1d));
            res["ErrorBorder"] = new SolidColorBrush(Color.FromRgb(0xd3, 0x2f, 0x2f));
            res["ErrorText"] = new SolidColorBrush(Color.FromRgb(0xef, 0x9a, 0x9a));
            res["SuccessBackground"] = new SolidColorBrush(Color.FromRgb(0x1d, 0x3a, 0x1d));
            res["SuccessText"] = new SolidColorBrush(Color.FromRgb(0x81, 0xc7, 0x84));
            res["HeaderColor"] = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0));
            res["PayloadColor"] = new SolidColorBrush(Color.FromRgb(0xdc, 0xdc, 0xaa));
        }
        else
        {
            res["AppBackground"] = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
            res["SurfaceBackground"] = new SolidColorBrush(Colors.White);
            res["SurfaceBorder"] = new SolidColorBrush(Color.FromRgb(0xd0, 0xd0, 0xd0));
            res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
            res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            res["TextDimmed"] = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x5a, 0x9e));
            res["TabBarBackground"] = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xe8));
            res["TabActiveBackground"] = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
            res["TabHoverBackground"] = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd));
            res["GridAltRow"] = new SolidColorBrush(Color.FromRgb(0xf0, 0xf0, 0xf0));
            res["ErrorBackground"] = new SolidColorBrush(Color.FromRgb(0xfd, 0xed, 0xed));
            res["ErrorBorder"] = new SolidColorBrush(Color.FromRgb(0xd3, 0x2f, 0x2f));
            res["ErrorText"] = new SolidColorBrush(Color.FromRgb(0xb7, 0x1c, 0x1c));
            res["SuccessBackground"] = new SolidColorBrush(Color.FromRgb(0xed, 0xf7, 0xed));
            res["SuccessText"] = new SolidColorBrush(Color.FromRgb(0x2e, 0x7d, 0x32));
            res["HeaderColor"] = new SolidColorBrush(Color.FromRgb(0x00, 0x77, 0x55));
            res["PayloadColor"] = new SolidColorBrush(Color.FromRgb(0x79, 0x55, 0x00));
        }

        res["AppFontSize"] = settings.FontSize;
        res["MonoFontSize"] = Math.Max(settings.FontSize - 1, 10.0);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }
}

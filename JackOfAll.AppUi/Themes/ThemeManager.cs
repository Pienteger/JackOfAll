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
            res["AppBackground"] = new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x26));
            res["SurfaceBackground"] = new SolidColorBrush(Color.FromRgb(0x1f, 0x20, 0x37));
            res["SurfaceBorder"] = new SolidColorBrush(Color.FromRgb(0x2d, 0x2e, 0x4a));
            res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xe2, 0xe2, 0xf0));
            res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x8f, 0x8f, 0xad));
            res["TextDimmed"] = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0x78));
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xf1));
            res["TabBarBackground"] = new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x25));
            res["TabActiveBackground"] = new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x26));
            res["TabHoverBackground"] = new SolidColorBrush(Color.FromRgb(0x25, 0x26, 0x40));
            res["GridAltRow"] = new SolidColorBrush(Color.FromRgb(0x1c, 0x1d, 0x30));
            res["ErrorBackground"] = new SolidColorBrush(Color.FromRgb(0x2d, 0x15, 0x20));
            res["ErrorBorder"] = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            res["ErrorText"] = new SolidColorBrush(Color.FromRgb(0xfc, 0xa5, 0xa5));
            res["SuccessBackground"] = new SolidColorBrush(Color.FromRgb(0x15, 0x2d, 0x1d));
            res["SuccessText"] = new SolidColorBrush(Color.FromRgb(0x86, 0xef, 0xac));
            res["HeaderColor"] = new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99));
            res["PayloadColor"] = new SolidColorBrush(Color.FromRgb(0xfb, 0xbf, 0x24));
            res["ScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0x3d, 0x3e, 0x5c));
            res["ScrollbarThumbHover"] = new SolidColorBrush(Color.FromRgb(0x52, 0x53, 0x80));
            res["ScrollbarTrack"] = new SolidColorBrush(Color.FromRgb(0x25, 0x26, 0x40));
        }
        else
        {
            res["AppBackground"] = new SolidColorBrush(Color.FromRgb(0xf8, 0xf9, 0xfc));
            res["SurfaceBackground"] = new SolidColorBrush(Colors.White);
            res["SurfaceBorder"] = new SolidColorBrush(Color.FromRgb(0xe2, 0xe4, 0xed));
            res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x1a, 0x1b, 0x2e));
            res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x64, 0x65, 0x8a));
            res["TextDimmed"] = new SolidColorBrush(Color.FromRgb(0x9b, 0x9c, 0xb8));
            res["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x4f, 0x46, 0xe5));
            res["TabBarBackground"] = new SolidColorBrush(Color.FromRgb(0xee, 0xf0, 0xf7));
            res["TabActiveBackground"] = new SolidColorBrush(Color.FromRgb(0xf8, 0xf9, 0xfc));
            res["TabHoverBackground"] = new SolidColorBrush(Color.FromRgb(0xe5, 0xe7, 0xf0));
            res["GridAltRow"] = new SolidColorBrush(Color.FromRgb(0xf3, 0xf4, 0xf9));
            res["ErrorBackground"] = new SolidColorBrush(Color.FromRgb(0xfe, 0xf2, 0xf2));
            res["ErrorBorder"] = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            res["ErrorText"] = new SolidColorBrush(Color.FromRgb(0xb9, 0x1c, 0x1c));
            res["SuccessBackground"] = new SolidColorBrush(Color.FromRgb(0xf0, 0xfd, 0xf4));
            res["SuccessText"] = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3d));
            res["HeaderColor"] = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
            res["PayloadColor"] = new SolidColorBrush(Color.FromRgb(0xd9, 0x77, 0x06));
            res["ScrollbarThumb"] = new SolidColorBrush(Color.FromRgb(0xc8, 0xca, 0xd5));
            res["ScrollbarThumbHover"] = new SolidColorBrush(Color.FromRgb(0xa0, 0xa2, 0xb0));
            res["ScrollbarTrack"] = new SolidColorBrush(Color.FromRgb(0xe8, 0xe9, 0xf0));
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

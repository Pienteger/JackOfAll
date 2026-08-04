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
            res["AppBackground"] = Frozen(0x18, 0x19, 0x26);
            res["SurfaceBackground"] = Frozen(0x1f, 0x20, 0x37);
            res["SurfaceBorder"] = Frozen(0x2d, 0x2e, 0x4a);
            res["TextPrimary"] = Frozen(0xe2, 0xe2, 0xf0);
            res["TextSecondary"] = Frozen(0x8f, 0x8f, 0xad);
            res["TextDimmed"] = Frozen(0x58, 0x58, 0x78);
            res["AccentBrush"] = Frozen(0x63, 0x66, 0xf1);
            res["TabBarBackground"] = Frozen(0x14, 0x15, 0x25);
            res["TabActiveBackground"] = Frozen(0x18, 0x19, 0x26);
            res["TabHoverBackground"] = Frozen(0x25, 0x26, 0x40);
            res["GridAltRow"] = Frozen(0x1c, 0x1d, 0x30);
            res["ErrorBackground"] = Frozen(0x2d, 0x15, 0x20);
            res["ErrorBorder"] = Frozen(0xef, 0x44, 0x44);
            res["ErrorText"] = Frozen(0xfc, 0xa5, 0xa5);
            res["SuccessBackground"] = Frozen(0x15, 0x2d, 0x1d);
            res["SuccessText"] = Frozen(0x86, 0xef, 0xac);
            res["HeaderColor"] = Frozen(0x34, 0xd3, 0x99);
            res["PayloadColor"] = Frozen(0xfb, 0xbf, 0x24);
            res["ScrollbarThumb"] = Frozen(0x3d, 0x3e, 0x5c);
            res["ScrollbarThumbHover"] = Frozen(0x52, 0x53, 0x80);
            res["ScrollbarTrack"] = Frozen(0x25, 0x26, 0x40);
            res["DangerBrush"] = Frozen(0xef, 0x44, 0x44);
            res["DangerHoverBrush"] = Frozen(0xdc, 0x26, 0x26);
            res["WarningBrush"] = Frozen(0xf5, 0x9e, 0x0b);
        }
        else
        {
            res["AppBackground"] = Frozen(0xf8, 0xf9, 0xfc);
            res["SurfaceBackground"] = Frozen(Colors.White);
            res["SurfaceBorder"] = Frozen(0xe2, 0xe4, 0xed);
            res["TextPrimary"] = Frozen(0x1a, 0x1b, 0x2e);
            res["TextSecondary"] = Frozen(0x64, 0x65, 0x8a);
            res["TextDimmed"] = Frozen(0x9b, 0x9c, 0xb8);
            res["AccentBrush"] = Frozen(0x4f, 0x46, 0xe5);
            res["TabBarBackground"] = Frozen(0xee, 0xf0, 0xf7);
            res["TabActiveBackground"] = Frozen(0xf8, 0xf9, 0xfc);
            res["TabHoverBackground"] = Frozen(0xe5, 0xe7, 0xf0);
            res["GridAltRow"] = Frozen(0xf3, 0xf4, 0xf9);
            res["ErrorBackground"] = Frozen(0xfe, 0xf2, 0xf2);
            res["ErrorBorder"] = Frozen(0xef, 0x44, 0x44);
            res["ErrorText"] = Frozen(0xb9, 0x1c, 0x1c);
            res["SuccessBackground"] = Frozen(0xf0, 0xfd, 0xf4);
            res["SuccessText"] = Frozen(0x15, 0x80, 0x3d);
            res["HeaderColor"] = Frozen(0x05, 0x96, 0x69);
            res["PayloadColor"] = Frozen(0xd9, 0x77, 0x06);
            res["ScrollbarThumb"] = Frozen(0xc8, 0xca, 0xd5);
            res["ScrollbarThumbHover"] = Frozen(0xa0, 0xa2, 0xb0);
            res["ScrollbarTrack"] = Frozen(0xe8, 0xe9, 0xf0);
            res["DangerBrush"] = Frozen(0xdc, 0x26, 0x26);
            res["DangerHoverBrush"] = Frozen(0xb9, 0x1c, 0x1c);
            res["WarningBrush"] = Frozen(0xd9, 0x77, 0x06);
        }

        res["AppFontSize"] = settings.FontSize;
        res["MonoFontSize"] = Math.Max(settings.FontSize - 1, 10.0);
        res["EditorFontSize"] = settings.EditorFontSize;

        Lib.ThemeNotifier.RaiseThemeChanged();
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
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

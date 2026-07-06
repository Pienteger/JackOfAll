using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Settings;
using AppThemeMode = JackOfAll.Lib.Settings.ThemeMode;

namespace JackOfAll.AppUi;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _applyTheme;
    private bool _initializing = true;

    public SettingsWindow(AppSettings settings, Action applyTheme)
    {
        InitializeComponent();
        _settings = settings;
        _applyTheme = applyTheme;

        FontSizeSlider.Value = settings.FontSize;
        FontSizeLabel.Text = $"{settings.FontSize:F0}";
        UpdatePreviewFontSizes(settings.FontSize);

        var themeIndex = settings.ThemeMode switch
        {
            AppThemeMode.Dark => 0,
            AppThemeMode.Light => 1,
            AppThemeMode.System => 2,
            _ => 0,
        };
        ThemeCombo.SelectedIndex = themeIndex;

        _initializing = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        if (ThemeCombo.SelectedItem is not ComboBoxItem item)
            return;

        _settings.ThemeMode = item.Tag?.ToString() switch
        {
            "Light" => AppThemeMode.Light,
            "System" => AppThemeMode.System,
            _ => AppThemeMode.Dark,
        };

        _applyTheme();
        _settings.Save();
    }

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing)
            return;

        var size = Math.Round(e.NewValue);
        _settings.FontSize = size;
        FontSizeLabel.Text = $"{size:F0}";
        UpdatePreviewFontSizes(size);

        _applyTheme();
        _settings.Save();
    }

    private void UpdatePreviewFontSizes(double size)
    {
        PreviewNormal.FontSize = size;
        PreviewMono.FontSize = Math.Max(size - 1, 10);
        PreviewSecondary.FontSize = size;
    }
}

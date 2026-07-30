using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Settings;
using AppAutoSaveMode = JackOfAll.Lib.Settings.AutoSaveMode;
using AppThemeMode = JackOfAll.Lib.Settings.ThemeMode;

namespace JackOfAll.AppUi;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _applyTheme;
    private bool _initializing = true;
    private readonly StackPanel[] _pages;

    public SettingsWindow(AppSettings settings, Action applyTheme)
    {
        InitializeComponent();
        _settings = settings;
        _applyTheme = applyTheme;

        _pages = [PageAppearance, PageEditor, PageAutoSave, PageAbout];

        FontSizeSlider.Value = settings.FontSize;
        FontSizeLabel.Text = $"{settings.FontSize:F0}";
        EditorFontSizeSlider.Value = settings.EditorFontSize;
        EditorFontSizeLabel.Text = $"{settings.EditorFontSize:F0}";
        UpdatePreviewFontSizes(settings.FontSize, settings.EditorFontSize);

        ThemeCombo.SelectedIndex = settings.ThemeMode switch
        {
            AppThemeMode.Dark => 0,
            AppThemeMode.Light => 1,
            AppThemeMode.System => 2,
            _ => 0,
        };

        AutoSaveCombo.SelectedIndex = settings.AutoSaveMode switch
        {
            AppAutoSaveMode.Off => 0,
            AppAutoSaveMode.AfterDelay => 1,
            AppAutoSaveMode.OnFocusChange => 2,
            _ => 1,
        };
        AutoSaveDelaySlider.Value = settings.AutoSaveDelayMs;
        AutoSaveDelayLabel.Text = $"{settings.AutoSaveDelayMs}ms";
        UpdateAutoSaveDelayVisibility(settings.AutoSaveMode);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.0";
        VersionLabel.Text = $"v{version}";
        AboutVersionLabel.Text = version;

        _initializing = false;
    }

    // ── Navigation ───────────────────────────────────────────────

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null) return;
        var idx = NavList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Appearance ───────────────────────────────────────────────

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (ThemeCombo.SelectedItem is not ComboBoxItem item) return;

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
        if (_initializing) return;

        var size = Math.Round(e.NewValue);
        _settings.FontSize = size;
        FontSizeLabel.Text = $"{size:F0}";
        UpdatePreviewFontSizes(size, _settings.EditorFontSize);

        _applyTheme();
        _settings.Save();
    }

    // ── Editor ───────────────────────────────────────────────────

    private void OnEditorFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;

        var size = Math.Round(e.NewValue);
        _settings.EditorFontSize = size;
        EditorFontSizeLabel.Text = $"{size:F0}";
        UpdatePreviewFontSizes(_settings.FontSize, size);

        _applyTheme();
        _settings.Save();
    }

    private void UpdatePreviewFontSizes(double uiSize, double editorSize)
    {
        PreviewNormal.FontSize = uiSize;
        PreviewMono.FontSize = Math.Max(uiSize - 1, 10);
        PreviewEditor.FontSize = editorSize;
        PreviewSecondary.FontSize = uiSize;
    }

    // ── Auto Save ────────────────────────────────────────────────

    private void OnAutoSaveModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (AutoSaveCombo.SelectedItem is not ComboBoxItem item) return;

        _settings.AutoSaveMode = item.Tag?.ToString() switch
        {
            "AfterDelay" => AppAutoSaveMode.AfterDelay,
            "OnFocusChange" => AppAutoSaveMode.OnFocusChange,
            _ => AppAutoSaveMode.Off,
        };

        UpdateAutoSaveDelayVisibility(_settings.AutoSaveMode);
        _settings.Save();
    }

    private void OnAutoSaveDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;

        var delay = (int)(Math.Round(e.NewValue / 100.0) * 100);
        _settings.AutoSaveDelayMs = delay;
        AutoSaveDelayLabel.Text = $"{delay}ms";
        _settings.Save();
    }

    private void UpdateAutoSaveDelayVisibility(AppAutoSaveMode mode)
    {
        var visible = mode == AppAutoSaveMode.AfterDelay;
        AutoSaveDelayPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutoSaveDelaySeparator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── About ────────────────────────────────────────────────────

    private void OnWebsiteLinkClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://mahmudx.com") { UseShellExecute = true });

    private void OnGitHubLinkClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/mahmudx") { UseShellExecute = true });
}

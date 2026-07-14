using System.Windows;
using JackOfAll.AppUi.Themes;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Factories;

namespace JackOfAll.AppUi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        var bus = new MessageBus();
        var registry = new ToolRegistry();

        void ApplyTheme() => ThemeManager.Apply(settings);

        registry.Register(new K8sToolFactory());
        registry.Register(new JwtToolFactory());
        registry.Register(new JsonToolFactory());
        registry.Register(new GeneratorToolFactory());
        registry.Register(new Base64ToolFactory());
        registry.Register(new HashToolFactory());
        registry.Register(new TimestampToolFactory());
        registry.Register(new EnvVarToolFactory());
        registry.Register(new PortScannerToolFactory());
        registry.Register(new SshKeyToolFactory());
        registry.Register(new ReminderToolFactory());
        registry.Register(new NotepadToolFactory());
        registry.Register(new WellbeingToolFactory());
        registry.Register(new GitHubToolFactory(settings));
        registry.Register(new ApiClientToolFactory());

        ApplyTheme();

        var window = new MainWindow(registry, bus, settings, ApplyTheme);
        window.Show();
    }
}

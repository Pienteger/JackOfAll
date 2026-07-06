using System.Windows;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Factories;

namespace JackOfAll.AppUi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var bus = new MessageBus();
        var registry = new ToolRegistry();

        registry.Register(new K8sToolFactory());
        registry.Register(new JwtToolFactory());

        var window = new MainWindow(registry, bus);
        window.Show();
    }
}

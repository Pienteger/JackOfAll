using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class K8sToolFactory : IToolFactory
{
    public string ToolId => "kubernetes";
    public string DisplayName => "Kubernetes";

    // Helm/wheel icon path
    public Geometry Icon => Geometry.Parse(
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 " +
        "0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-13h2v6h-2zm0 8h2v2h-2z");

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new K8sToolView(bus, openTool);
}

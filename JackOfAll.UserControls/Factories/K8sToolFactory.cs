using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class K8sToolFactory : IToolFactory
{
    public string ToolId => "kubernetes";
    public string DisplayName => "Kubernetes";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new K8sToolView(bus, openTool);
}

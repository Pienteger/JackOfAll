using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class PortScannerToolFactory : IToolFactory
{
    public string ToolId => "portscan";
    public string DisplayName => "Port Scanner";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new PortScannerToolView();
}

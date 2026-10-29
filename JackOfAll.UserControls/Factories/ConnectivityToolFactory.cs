using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class ConnectivityToolFactory : IToolFactory
{
    public string ToolId => "connectivity";
    public string DisplayName => "Connectivity";
    public string IconGlyph => "M12 17.5a2 2 0 1 0 0.01 0ZM6.91 13.85A7.6 7.6 0 0 1 17.09 13.85L15.35 15.78A5 5 0 0 0 8.65 15.78ZM3.84 10.43A12.2 12.2 0 0 1 20.16 10.43L18.42 12.37A9.6 9.6 0 0 0 5.58 12.37ZM0.76 7.02A16.8 16.8 0 0 1 23.24 7.02L21.5 8.95A14.2 14.2 0 0 0 2.5 8.95Z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new ConnectivityToolView();
}

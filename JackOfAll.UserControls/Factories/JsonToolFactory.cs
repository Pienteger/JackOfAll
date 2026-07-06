using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class JsonToolFactory : IToolFactory
{
    public string ToolId => "json";
    public string DisplayName => "JSON Tools";
    public string IconGlyph => "{}";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool) =>
        new JsonToolView(bus);
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class JwtToolFactory : IToolFactory
{
    public string ToolId => "jwt";
    public string DisplayName => "JWT Decoder";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new JwtToolView(bus);
}

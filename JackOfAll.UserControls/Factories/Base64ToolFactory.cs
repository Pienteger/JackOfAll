using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class Base64ToolFactory : IToolFactory
{
    public string ToolId => "base64";
    public string DisplayName => "Base64";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new Base64ToolView();
}

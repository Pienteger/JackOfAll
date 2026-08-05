using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class Base64ToolFactory : IToolFactory
{
    public string ToolId => "base64";
    public string DisplayName => "Base64";
    public string IconGlyph => "m6 20l1-4H3l.5-2h4l1-4h-4L5 8h4l1-4h2l-1 4h4l1-4h2l-1 4h4l-.5 2h-4l-1 4h4l-.5 2h-4l-1 4h-2l1-4H9l-1 4zm3.5-6h4l1-4h-4z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new Base64ToolView();
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class UndertowToolFactory : IToolFactory
{
    public string ToolId => "undertow";
    public string DisplayName => "Undertow";
    public string IconGlyph => "M12 4a10 10 0 0 0-8.66 15h17.32A10 10 0 0 0 12 4m0 2a8 8 0 0 1 7.4 11H4.6A8 8 0 0 1 12 6m4.2 2.4l-5 4.3a1.5 1.5 0 1 0 2.1 2.1z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new UndertowToolView();
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class SpaceLensToolFactory : IToolFactory
{
    public string ToolId => "spacelens";
    public string DisplayName => "SpaceLens";
    public string IconGlyph => "M3 3h10v11H3zm12 0h6v7h-6zm0 9h6v9h-6zM3 16h10v5H3z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new SpaceLensToolView();
}

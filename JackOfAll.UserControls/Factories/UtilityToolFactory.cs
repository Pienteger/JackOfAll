using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class UtilityToolFactory : IToolFactory
{
    public string ToolId => "utilities";
    public string DisplayName => "Utilities";
    public string IconGlyph => "\uE71D";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new UtilityToolView();
}
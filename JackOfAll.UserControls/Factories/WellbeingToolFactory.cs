using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class WellbeingToolFactory : IToolFactory
{
    public string ToolId => "wellbeing";
    public string DisplayName => "Wellbeing";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new WellbeingToolView();
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class TimestampToolFactory : IToolFactory
{
    public string ToolId => "timestamp";
    public string DisplayName => "Timestamp";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new TimestampToolView();
}

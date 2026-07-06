using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class ReminderToolFactory : IToolFactory
{
    public string ToolId => "reminders";
    public string DisplayName => "Reminders";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new ReminderToolView();
}

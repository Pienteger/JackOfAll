using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class ReminderToolFactory : IToolFactory
{
    public string ToolId => "reminders";
    public string DisplayName => "Reminders";
    public string IconGlyph => "M4 19v-2h2v-7q0-2.075 1.25-3.687T10.5 4.2v-.7q0-.625.438-1.062T12 2t1.063.438T13.5 3.5v.7q2 .5 3.25 2.113T18 10v7h2v2zm8 3q-.825 0-1.412-.587T10 20h4q0 .825-.587 1.413T12 22";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new ReminderToolView();
}

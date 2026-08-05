using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class DiffToolFactory : IToolFactory
{
    public string ToolId => "diff";
    public string DisplayName => "Diff";
    public string IconGlyph => "M10 23v-2H5q-.825 0-1.412-.587T3 19V5q0-.825.588-1.412T5 3h5V1h2v22zm-5-5h5v-6zm9 3v-9l5 6V5h-5V3h5q.825 0 1.413.588T21 5v14q0 .825-.587 1.413T19 21z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new DiffToolView();
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls.Factories;

public sealed class FerryToolFactory : IToolFactory
{
    private readonly FerryService _service = new();
    public string ToolId => "ferry";
    public string DisplayName => "Ferry";
    public string IconGlyph => "M8 13V5.825L5.425 8.4L4 7l5-5l5 5l-1.425 1.4L10 5.825V13zm7 9l-5-5l1.425-1.4L14 18.175V11h2v7.175l2.575-2.575L20 17z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new FerryToolView(_service);
}

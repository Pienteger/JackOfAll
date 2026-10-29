using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class HushToolFactory : IToolFactory
{
    public string ToolId => "hush";
    public string DisplayName => "Hush";
    public string IconGlyph => "M4 10h2v4H4zm4-4h2v12H8zm4-3h2v18h-2zm4 5h2v8h-2zm4 3h2v2h-2z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new HushToolView();
}

using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class DiffToolFactory : IToolFactory
{
    public string ToolId => "diff";
    public string DisplayName => "Diff";
    public string IconGlyph => "\uE8AB";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new DiffToolView();
}

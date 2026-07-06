using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class GeneratorToolFactory : IToolFactory
{
    public string ToolId => "generator";
    public string DisplayName => "Generator";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new GeneratorToolView();
}

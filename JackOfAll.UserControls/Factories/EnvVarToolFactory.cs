using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class EnvVarToolFactory : IToolFactory
{
    public string ToolId => "envvar";
    public string DisplayName => "Env Vars";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new EnvVarToolView();
}

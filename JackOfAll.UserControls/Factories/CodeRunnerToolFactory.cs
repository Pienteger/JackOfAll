using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class CodeRunnerToolFactory : IToolFactory
{
    public string ToolId => "code-runner";
    public string DisplayName => "Code Runner";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new CodeRunnerToolView();
}

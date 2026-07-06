using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class SshKeyToolFactory : IToolFactory
{
    public string ToolId => "sshkeys";
    public string DisplayName => "SSH Keys";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new SshKeyToolView();
}

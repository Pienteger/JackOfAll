using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class HashToolFactory : IToolFactory
{
    public string ToolId => "hash";
    public string DisplayName => "Hash";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new HashToolView();
}

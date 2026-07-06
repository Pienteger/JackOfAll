using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class NotepadToolFactory : IToolFactory
{
    public string ToolId => "notepad";
    public string DisplayName => "Notepad";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new NotepadToolView();
}

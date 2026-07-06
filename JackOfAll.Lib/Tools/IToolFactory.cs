using System.Windows.Controls;
using JackOfAll.Lib.Messaging;

namespace JackOfAll.Lib.Tools;

public interface IToolFactory
{
    string ToolId { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool);
}

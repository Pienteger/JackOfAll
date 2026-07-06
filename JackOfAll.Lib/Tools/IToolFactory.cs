using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;

namespace JackOfAll.Lib.Tools;

public interface IToolFactory
{
    string ToolId { get; }
    string DisplayName { get; }
    Geometry Icon { get; }
    UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool);
}

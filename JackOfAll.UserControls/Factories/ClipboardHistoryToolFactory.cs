using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services;

namespace JackOfAll.UserControls.Factories;

public sealed class ClipboardHistoryToolFactory : IToolFactory
{
    private readonly ClipboardHistoryService _service = new();
    public string ToolId => "clipboard";
    public string DisplayName => "Clipboard";
    public string IconGlyph => "M5 21q-.825 0-1.412-.587T3 19V5q0-.825.588-1.412T5 3h4.175q.275-.875 1.075-1.437T12 1q1 0 1.788.563T14.85 3H19q.825 0 1.413.588T21 5v14q0 .825-.587 1.413T19 21zm0-2h14V5h-2v3H7V5H5zm7.713-14.288Q13 4.425 13 4t-.288-.712T12 3t-.712.288T11 4t.288.713T12 5t.713-.288";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new ClipboardHistoryToolView(_service);

    /// <summary>Starts background recording and the quick-picker shortcut once the main window exists.</summary>
    public void Start(Window mainWindow, AppSettings settings) => _service.Start(mainWindow, settings);
}

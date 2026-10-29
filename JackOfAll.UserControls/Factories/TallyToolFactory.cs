using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;
using JackOfAll.UserControls.Services.Tally;

namespace JackOfAll.UserControls.Factories;

public sealed class TallyToolFactory : IToolFactory
{
    private readonly TallyRecorder _recorder = new();
    public string ToolId => "tally";
    public string DisplayName => "Tally";
    public string IconGlyph => "M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20m0 2a8 8 0 1 1 0 16a8 8 0 0 1 0-16m-1 3v6l5 3l1-1.7l-4-2.4V7z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new TallyToolView(_recorder);

    /// <summary>Starts recording when the app starts, whether or not the Tally tab is open.</summary>
    public void Start(Window mainWindow) => _recorder.Start(mainWindow);
}

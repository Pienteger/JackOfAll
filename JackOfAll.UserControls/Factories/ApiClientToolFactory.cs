using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class ApiClientToolFactory : IToolFactory
{
    public string ToolId => "apiclient";
    public string DisplayName => "API Client";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool) =>
        new ApiClientToolView();
}
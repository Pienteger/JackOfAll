using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class ApiClientToolFactory : IToolFactory
{
    public string ToolId => "apiclient";
    public string DisplayName => "API Client";
    public string IconGlyph => "M3 20v-6l8-2l-8-2V4l19 8z";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool) =>
        new ApiClientToolView();
}

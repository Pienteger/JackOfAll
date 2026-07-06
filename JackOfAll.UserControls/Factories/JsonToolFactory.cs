using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class JsonToolFactory : IToolFactory
{
    public string ToolId => "json";
    public string DisplayName => "JSON Tools";

    // Curly braces icon
    public Geometry Icon => Geometry.Parse(
        "M4 18V14.3C4 13.4716 3.32843 12.8 2.5 12.8H2V11.2H2.5C3.32843 11.2 4 10.5284 " +
        "4 9.7V6C4 4.34315 5.34315 3 7 3H8V5H7C6.44772 5 6 5.44772 6 6V10.1C6 10.9284 " +
        "5.42843 11.6 4.7 11.92V12.08C5.42843 12.4 6 13.0716 6 13.9V18C6 18.5523 6.44772 " +
        "19 7 19H8V21H7C5.34315 21 4 19.6569 4 18ZM20 14.3V18C20 19.6569 18.6569 21 17 " +
        "21H16V19H17C17.5523 19 18 18.5523 18 18V13.9C18 13.0716 18.5716 12.4 19.3 12.08V11.92C18.5716 " +
        "11.6 18 10.9284 18 10.1V6C18 5.44772 17.5523 5 17 5H16V3H17C18.6569 3 20 4.34315 " +
        "20 6V9.7C20 10.5284 20.6716 11.2 21.5 11.2H22V12.8H21.5C20.6716 12.8 20 13.4716 20 14.3Z");

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new JsonToolView(bus);
}

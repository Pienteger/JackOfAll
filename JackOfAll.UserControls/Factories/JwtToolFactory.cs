using System.Windows.Controls;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class JwtToolFactory : IToolFactory
{
    public string ToolId => "jwt";
    public string DisplayName => "JWT Decoder";

    // Key icon path
    public Geometry Icon => Geometry.Parse(
        "M12.65 10C11.83 7.67 9.61 6 7 6c-3.31 0-6 2.69-6 6s2.69 6 6 6c2.61 " +
        "0 4.83-1.67 5.65-4H17v4h4v-4h2v-4H12.65zM7 14c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z");

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new JwtToolView(bus);
}

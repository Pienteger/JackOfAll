using System.Windows.Controls;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Settings;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls.Factories;

public sealed class GitHubToolFactory : IToolFactory
{
    private readonly AppSettings _settings;

    public GitHubToolFactory(AppSettings settings) => _settings = settings;

    public string ToolId => "github";
    public string DisplayName => "GitHub";
    public string IconGlyph => "";

    public UserControl CreateView(IMessageBus bus, Action<OpenToolMessage> openTool)
        => new GitHubToolView(_settings);
}



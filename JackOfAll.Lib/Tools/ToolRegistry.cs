using System.Collections.ObjectModel;

namespace JackOfAll.Lib.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolFactory> _factories = new();

    public ReadOnlyCollection<IToolFactory> Tools => _factories.Values.ToList().AsReadOnly();

    public void Register(IToolFactory factory)
    {
        _factories[factory.ToolId] = factory;
    }

    public IToolFactory? Get(string toolId)
    {
        _factories.TryGetValue(toolId, out var factory);
        return factory;
    }
}

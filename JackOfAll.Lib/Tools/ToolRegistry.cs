using System.Collections.ObjectModel;

namespace JackOfAll.Lib.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolFactory> _factories = new();
    private ReadOnlyCollection<IToolFactory>? _cachedTools;

    public ReadOnlyCollection<IToolFactory> Tools => _cachedTools ??= _factories.Values.ToList().AsReadOnly();

    public void Register(IToolFactory factory)
    {
        _factories[factory.ToolId] = factory;
        _cachedTools = null;
    }

    public IToolFactory? Get(string toolId)
    {
        _factories.TryGetValue(toolId, out var factory);
        return factory;
    }
}

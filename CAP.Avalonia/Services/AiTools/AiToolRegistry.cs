namespace CAP.Avalonia.Services.AiTools;

/// <summary>
/// Registry that aggregates all registered <see cref="IAiTool"/> implementations.
/// Tools are injected via DI as <see cref="IEnumerable{T}"/> of <see cref="IAiTool"/>.
/// Add a new tool by implementing <see cref="IAiTool"/> and registering it in App.axaml.cs —
/// no changes needed to this class.
/// </summary>
public class AiToolRegistry : IAiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools;

    /// <summary>
    /// Initializes the registry with all tools provided by the DI container.
    /// </summary>
    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IAiTool? GetTool(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <inheritdoc/>
    public IReadOnlyList<IAiTool> GetAllTools() =>
        _tools.Values.ToList();
}

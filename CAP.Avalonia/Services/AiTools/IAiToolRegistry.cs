namespace CAP.Avalonia.Services.AiTools;

/// <summary>
/// Provides lookup and enumeration of all registered AI tools.
/// </summary>
public interface IAiToolRegistry
{
    /// <summary>Returns the tool with the given name, or null if not registered.</summary>
    IAiTool? GetTool(string name);

    /// <summary>Returns all registered tools.</summary>
    IReadOnlyList<IAiTool> GetAllTools();
}

namespace CAP_Core.Components;

/// <summary>
/// Represents metadata about a loaded PDK (Process Design Kit).
/// Tracks PDK state for filtering and management.
/// </summary>
public class PdkInfo
{
    /// <summary>
    /// Display name of the PDK (e.g., "SiEPIC EBeam", "Demo PDK").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Full file path to the PDK JSON file.
    /// Null for built-in component templates.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Number of components provided by this PDK.
    /// </summary>
    public int ComponentCount { get; private set; }

    /// <summary>
    /// Whether this PDK is bundled with the application (auto-loaded at startup).
    /// </summary>
    public bool IsBundled { get; }

    /// <summary>
    /// Whether this PDK's components are currently visible in the library.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Creates a new PDK info instance.
    /// </summary>
    /// <param name="name">Display name of the PDK.</param>
    /// <param name="filePath">Full path to PDK file (null for built-in components).</param>
    /// <param name="isBundled">True if this is a bundled PDK.</param>
    /// <param name="componentCount">Number of components in this PDK.</param>
    public PdkInfo(string name, string? filePath, bool isBundled, int componentCount)
    {
        Name = name;
        FilePath = filePath;
        IsBundled = isBundled;
        ComponentCount = componentCount;
        IsEnabled = true; // Enabled by default
    }

    /// <summary>
    /// Updates the component count (used when components are loaded).
    /// </summary>
    public void UpdateComponentCount(int count)
    {
        ComponentCount = count;
    }

    /// <summary>
    /// Returns a display string describing the PDK source (bundled/user-loaded).
    /// </summary>
    public string SourceType => IsBundled ? "Bundled" : "User";

    public override string ToString() => $"{Name} ({ComponentCount} components, {SourceType})";
}

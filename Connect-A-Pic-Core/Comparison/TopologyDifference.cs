namespace CAP_Core.Comparison;

/// <summary>
/// Categorizes a topology difference between two designs.
/// </summary>
public enum DifferenceKind
{
    /// <summary>Component exists only in Design A.</summary>
    OnlyInA,

    /// <summary>Component exists only in Design B.</summary>
    OnlyInB,

    /// <summary>Component exists in both but with different properties.</summary>
    Modified,

    /// <summary>Connection exists only in Design A.</summary>
    ConnectionOnlyInA,

    /// <summary>Connection exists only in Design B.</summary>
    ConnectionOnlyInB
}

/// <summary>
/// A single difference found between two design snapshots.
/// </summary>
public class TopologyDifference
{
    /// <summary>
    /// The kind of difference.
    /// </summary>
    public DifferenceKind Kind { get; }

    /// <summary>
    /// Human-readable description of the difference.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Name of the component or connection involved.
    /// </summary>
    public string ElementName { get; }

    public TopologyDifference(DifferenceKind kind, string elementName, string description)
    {
        Kind = kind;
        ElementName = elementName;
        Description = description;
    }
}

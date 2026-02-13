namespace CAP_Core.Components;

/// <summary>
/// Represents usage statistics for a single component type.
/// </summary>
public class ComponentUsageRecord
{
    /// <summary>
    /// Gets the component type identifier string.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// Gets the component type number.
    /// </summary>
    public int TypeNumber { get; }

    /// <summary>
    /// Gets the number of instances of this component type.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Creates a new component usage record.
    /// </summary>
    /// <param name="identifier">The component type identifier.</param>
    /// <param name="typeNumber">The component type number.</param>
    /// <param name="count">The number of instances.</param>
    public ComponentUsageRecord(string identifier, int typeNumber, int count)
    {
        Identifier = identifier;
        TypeNumber = typeNumber;
        Count = count;
    }
}

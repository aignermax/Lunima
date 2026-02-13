namespace CAP_Core.Comparison;

/// <summary>
/// Immutable snapshot of a loaded design for comparison purposes.
/// Contains the component and connection data extracted from a .cappro file.
/// </summary>
public class DesignSnapshot
{
    /// <summary>
    /// Display name for this design (typically the file name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Components in the design.
    /// </summary>
    public IReadOnlyList<SnapshotComponent> Components { get; }

    /// <summary>
    /// Connections between components.
    /// </summary>
    public IReadOnlyList<SnapshotConnection> Connections { get; }

    public DesignSnapshot(
        string name,
        IReadOnlyList<SnapshotComponent> components,
        IReadOnlyList<SnapshotConnection> connections)
    {
        Name = name;
        Components = components;
        Connections = connections;
    }
}

/// <summary>
/// A component within a design snapshot.
/// </summary>
public class SnapshotComponent
{
    public string TemplateName { get; }
    public double X { get; }
    public double Y { get; }
    public string Identifier { get; }
    public int Rotation { get; }

    public SnapshotComponent(
        string templateName,
        double x,
        double y,
        string identifier,
        int rotation)
    {
        TemplateName = templateName;
        X = x;
        Y = y;
        Identifier = identifier;
        Rotation = rotation;
    }
}

/// <summary>
/// A connection within a design snapshot.
/// </summary>
public class SnapshotConnection
{
    public int StartComponentIndex { get; }
    public string StartPinName { get; }
    public int EndComponentIndex { get; }
    public string EndPinName { get; }

    public SnapshotConnection(
        int startComponentIndex,
        string startPinName,
        int endComponentIndex,
        string endPinName)
    {
        StartComponentIndex = startComponentIndex;
        StartPinName = startPinName;
        EndComponentIndex = endComponentIndex;
        EndPinName = endPinName;
    }
}

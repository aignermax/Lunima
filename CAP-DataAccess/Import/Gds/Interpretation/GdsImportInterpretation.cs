namespace CAP_DataAccess.Import.Gds.Interpretation;

/// <summary>Portable, explicit interpretation of an immutable GDS source.</summary>
public sealed record GdsImportInterpretation
{
    /// <summary>Detaches editable interpretation collections from a pending save.</summary>
    public GdsImportInterpretation Copy() => this with
    {
        Layers = [.. Layers],
        Cells = Cells.Select(cell => cell with
        {
            Pins = cell.Pins is null ? null : [.. cell.Pins],
            ModelPinMap = new(cell.ModelPinMap, cell.ModelPinMap.Comparer)
        }).ToList(),
        Groups = Groups.Select(group => group with
        {
            CellNames = [.. group.CellNames], InstanceNames = [.. group.InstanceNames]
        }).ToList()
    };

    /// <summary>Persistence schema version.</summary>
    public int Version { get; init; } = 1;
    /// <summary>SHA-256 of the reviewed source bytes.</summary>
    public string SourceSha256 { get; init; } = string.Empty;
    /// <summary>Reviewed layout root.</summary>
    public string TopCellName { get; init; } = string.Empty;
    /// <summary>Confirmed layer semantics.</summary>
    public List<GdsLayerAssignment> Layers { get; init; } = [];
    /// <summary>Cell-specific decisions.</summary>
    public List<GdsCellInterpretation> Cells { get; init; } = [];
    /// <summary>Logical groups of intact source cell instances.</summary>
    public List<GdsInterpretationGroup> Groups { get; init; } = [];
}

/// <summary>Reviewed ports and optional simulation model without replacing geometry.</summary>
public sealed record GdsCellInterpretation
{
    /// <summary>Case-sensitive original cell name.</summary>
    public string CellName { get; init; } = string.Empty;
    /// <summary>Replace this hierarchy container with its children.</summary>
    public bool ExpandChildren { get; init; }
    /// <summary>Optional library display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Complete reviewed port list; null retains detection, empty removes all ports.</summary>
    public List<DetectedPin>? Pins { get; init; }
    /// <summary>Explicit model library; absent means geometry-only.</summary>
    public string? ModelPdkName { get; init; }
    /// <summary>Explicit model name.</summary>
    public string? ModelComponentName { get; init; }
    /// <summary>Imported port name to model port name; absent entries use identical names.</summary>
    public Dictionary<string, string> ModelPinMap { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Groups explicit imported occurrences, or legacy sets of whole cell types.</summary>
public sealed record GdsInterpretationGroup
{
    /// <summary>Group display name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Original cell names, each owned by at most one group.</summary>
    public List<string> CellNames { get; init; } = [];
    /// <summary>Explicit placed occurrences; empty retains legacy cell-type grouping.</summary>
    public List<string> InstanceNames { get; init; } = [];
}

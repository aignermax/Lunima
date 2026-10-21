namespace CAP.Avalonia.Services.GdsImport;

/// <summary>A persistable explicit simulation model available for geometry-preserving binding.</summary>
public sealed record GdsModelCandidate(string PdkName, string ComponentName, IReadOnlyList<string> PinNames)
{
    public IReadOnlyDictionary<string, string> PinDomains { get; init; } = new Dictionary<string, string>();
}

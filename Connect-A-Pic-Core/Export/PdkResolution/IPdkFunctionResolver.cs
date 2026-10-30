namespace CAP_Core.Export.PdkResolution;

/// <summary>Checks an ordered batch of component functions against the configured Python backends.</summary>
public interface IPdkFunctionResolver
{
    Task<PdkResolutionReport> ResolveAsync(IReadOnlyList<PdkResolutionEntry> entries, CancellationToken cancellationToken = default);
}

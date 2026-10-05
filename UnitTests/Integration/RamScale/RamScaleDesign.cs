namespace UnitTests.Integration.RamScale;

/// <summary>The built RAM design: .lun JSON plus the census the spike reports (issue #1337).</summary>
internal sealed class RamScaleDesign
{
    public required string Json { get; init; }
    public required int GateCount { get; init; }
    public required int WireCount { get; init; }
    public required double ChipWidthMicrometers { get; init; }
    public required double ChipHeightMicrometers { get; init; }
    public required IReadOnlyList<string> AddressSignals { get; init; }
    public required IReadOnlyList<string> DataSignals { get; init; }
    public required IReadOnlyList<string> ReadTaps { get; init; }

    /// <summary>The LOAD input signal name (shared by all geometries).</summary>
    public const string LoadSignal = "LOAD";

    /// <summary>Writes the design JSON to a fresh temp .lun file and returns its path.</summary>
    public string WriteToTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ram-scale-{Guid.NewGuid():N}.lun");
        File.WriteAllText(path, Json);
        return path;
    }
}

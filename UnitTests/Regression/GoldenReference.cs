using System.Text.Json;

namespace UnitTests.Regression;

/// <summary>
/// One truth-table row of a logic-gate golden: drive the listed inputs at the
/// given wavelength and expect these output transmissions. Rows are reviewed
/// in the PR like snapshot data; the gate fails on any wrong row.
/// </summary>
public sealed class GoldenTruthRow
{
    /// <summary>Wavelength (nm) the row is evaluated at.</summary>
    public int WavelengthNm { get; set; }

    /// <summary>Inputs driven for this row (component.pin + power).</summary>
    public List<GoldenInput> Inputs { get; set; } = new();

    /// <summary>Expected linear transmission per output pin reference.</summary>
    public Dictionary<string, double> Expected { get; set; } = new();
}

/// <summary>
/// Pinned reference of one shipped example: the expected transmission spectrum
/// per output pin plus optional truth-table rows. Generated once with
/// <c>LUNIMA_UPDATE_GOLDEN=1</c> and reviewed in the PR.
/// </summary>
public sealed class GoldenReference
{
    /// <summary>Sweep wavelengths (nm) this reference was generated with.</summary>
    public int[] WavelengthsNm { get; set; } = Array.Empty<int>();

    /// <summary>Expected linear transmission per output pin reference, one value per wavelength.</summary>
    public Dictionary<string, double[]> Transmission { get; set; } = new();

    /// <summary>Optional truth-table rows (logic-gate examples).</summary>
    public List<GoldenTruthRow> TruthTable { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Reads a golden file.</summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>Parsed reference.</returns>
    public static GoldenReference Read(string path)
    {
        var reference = JsonSerializer.Deserialize<GoldenReference>(
            File.ReadAllText(path), Options);
        return reference ?? new GoldenReference();
    }

    /// <summary>Writes a golden file (update mode). Creates the directory if needed.</summary>
    /// <param name="path">Absolute path.</param>
    /// <param name="reference">Reference to serialize.</param>
    public static void Write(string path, GoldenReference reference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(reference, Options));
    }
}

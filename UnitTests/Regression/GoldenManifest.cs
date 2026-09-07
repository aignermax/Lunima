using System.Text.Json;

namespace UnitTests.Regression;

/// <summary>
/// One driven input of a golden-design run: a resolved pin reference
/// (<c>componentIdentifier.pinName</c>) and the optical power injected there.
/// </summary>
public sealed class GoldenInput
{
    /// <summary>Pin reference in the form <c>componentIdentifier.pinName</c>.</summary>
    public string Pin { get; set; } = "";

    /// <summary>Linear input power injected at the pin (default 1.0).</summary>
    public double Power { get; set; } = 1.0;
}

/// <summary>
/// One entry of <c>examples/examples.json</c>: the shipped .lun file plus the
/// simulation setup its golden gate run uses (sweep range, tolerance, driven
/// inputs, measured outputs). The expected values live in the golden JSON.
/// </summary>
public sealed class GoldenManifestEntry
{
    /// <summary>File name of the shipped example, relative to <c>examples/</c>.</summary>
    public string File { get; set; } = "";

    /// <summary>Sweep start wavelength (nm).</summary>
    public int StartNm { get; set; }

    /// <summary>Sweep end wavelength (nm).</summary>
    public int EndNm { get; set; }

    /// <summary>Number of evenly spaced sweep samples (2 – 500).</summary>
    public int StepCount { get; set; }

    /// <summary>Relative comparison tolerance (default 1e-3).</summary>
    public double Tolerance { get; set; } = DefaultTolerance;

    /// <summary>Driven inputs (component.pin + power).</summary>
    public List<GoldenInput> Inputs { get; set; } = new();

    /// <summary>Measured output pin references.</summary>
    public List<string> Outputs { get; set; } = new();

    /// <summary>Default relative tolerance used when the manifest does not specify one.</summary>
    public const double DefaultTolerance = 1e-3;

    /// <summary>Display name: the example file name without extension.</summary>
    public string Name => Path.GetFileNameWithoutExtension(File);

    /// <summary>Golden reference file name derived from the example file name.</summary>
    public string GoldenFileName => Name + ".json";
}

/// <summary>
/// Root of <c>examples/examples.json</c>.
/// </summary>
public sealed class GoldenManifestRoot
{
    /// <summary>Configured examples.</summary>
    public List<GoldenManifestEntry> Examples { get; set; } = new();
}

/// <summary>
/// Locates the repository root and loads <c>examples/examples.json</c>.
/// The root is the first ancestor of the test-output directory that contains
/// both <c>examples/</c> and <c>UnitTests/</c>.
/// </summary>
public static class GoldenManifest
{
    /// <summary>Folder (under <c>UnitTests/Regression/golden</c>) holding golden JSONs.</summary>
    public const string GoldenFolderName = "golden";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Resolves the repository root directory.</summary>
    /// <returns>Path containing both <c>examples/</c> and <c>UnitTests/</c>.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor matches.</exception>
    public static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "examples"))
                && Directory.Exists(Path.Combine(current.FullName, "UnitTests")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root (examples/ + UnitTests/) not found walking up from "
            + AppContext.BaseDirectory);
    }

    /// <summary>Loads and parses the manifest.</summary>
    /// <param name="repositoryRoot">Root from <see cref="LocateRepositoryRoot"/>.</param>
    /// <returns>Parsed entries.</returns>
    public static List<GoldenManifestEntry> Load(string repositoryRoot)
    {
        string manifestPath = ManifestPath(repositoryRoot);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                "examples/examples.json manifest missing — every shipped example must be listed there.",
                manifestPath);

        var root = JsonSerializer.Deserialize<GoldenManifestRoot>(
            File.ReadAllText(manifestPath), Options);
        return root?.Examples ?? new List<GoldenManifestEntry>();
    }

    /// <summary>Path of the manifest file.</summary>
    public static string ManifestPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "examples", "examples.json");

    /// <summary>Path of the shipped examples directory.</summary>
    public static string ExamplesDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "examples");

    /// <summary>Path of the golden-reference directory.</summary>
    public static string GoldenDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "UnitTests", "Regression", GoldenFolderName);

    /// <summary>Path of an entry's golden reference file.</summary>
    public static string GoldenPath(string repositoryRoot, GoldenManifestEntry entry) =>
        Path.Combine(GoldenDirectory(repositoryRoot), entry.GoldenFileName);

    /// <summary>Environment variable enabling golden regeneration instead of comparison.</summary>
    public const string UpdateSwitchName = "LUNIMA_UPDATE_GOLDEN";

    /// <summary>True when golden regeneration is enabled via <see cref="UpdateSwitchName"/>.</summary>
    public static bool IsUpdateMode()
    {
        var value = Environment.GetEnvironmentVariable(UpdateSwitchName);
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

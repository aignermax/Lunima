using Shouldly;
using Xunit;

namespace UnitTests.Architecture.Packaging;

/// <summary>
/// Guards that every helper script the app resolves under <c>scripts/</c> is published beside the
/// binary by <c>CAP.Desktop.csproj</c>. Such a lookup works in a repository checkout, where the
/// walk-up finds the repository's own <c>scripts/</c>, and fails in every installed build when the
/// script was left out of the bundle. The portable archives, the MSI harvest and the macOS
/// <c>.app</c> all package the publish output, so the <c>Content</c> items are the single source.
/// </summary>
public class ShippedHelperScriptsTests
{
    /// <summary>
    /// Debug tools no installed user can reach, so they deliberately stay out of the bundle.
    /// Remove an entry (and ship the script) as soon as a view exposes the tool.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DevOnlyScripts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // GDS coordinate debugging (CLAUDE.md §11), run from a checkout.
        ["scripts/extract_gds_coords.py"] = "GdsCoordinateExtractor: GdsCoordExtractViewModel has no AXAML view",
        ["scripts/compare_gds_coords.py"] = "GdsCoordinateComparisonService: GdsCoordinateComparisonViewModel has no AXAML view",
    };

    private static readonly string RepositoryRoot = SourceSizePolicy.RepositoryRoot();
    private static readonly IReadOnlyList<HelperScriptLookup> Lookups = HelperScriptLookupScanner.Scan(RepositoryRoot);

    [Fact]
    public void Scan_FindsTheFindScriptAndLiteralPathLookups()
    {
        Lookups.ShouldNotBeEmpty("the scan found no helper-script lookups; a refactor must not let this guard pass vacuously");
        var paths = Lookups.Select(lookup => lookup.ScriptPath).ToHashSet();
        paths.ShouldContain("scripts/mode_solve.py", "PythonResolution.FindScript(\"mode_solve.py\") was not found");
        paths.ShouldContain("scripts/parse_pdk.py", "the literal \"scripts\", \"parse_pdk.py\" lookup was not found");
    }

    [Fact]
    public void EveryLookup_NamesItsScriptLiterally()
    {
        var unresolved = Lookups.Where(lookup => lookup.ScriptPath == null).Select(lookup => lookup.Location).ToList();

        unresolved.ShouldBeEmpty(
            "PythonResolution.FindScript with a non-literal argument can't be checked against the bundle: " +
            string.Join(", ", unresolved));
    }

    [Fact]
    public void EveryResolvedScript_IsShippedBesideTheBinary()
    {
        var manifest = DesktopContentManifest.Load(DesktopProjectFile());

        var missing = Lookups
            .Where(lookup => lookup.ScriptPath != null && !DevOnlyScripts.ContainsKey(lookup.ScriptPath))
            .GroupBy(lookup => lookup.ScriptPath!, StringComparer.Ordinal)
            .Where(group => !manifest.Ships(group.Key))
            .Select(group => $"{group.Key} (resolved at {string.Join(", ", group.Select(lookup => lookup.Location))})")
            .ToList();

        missing.ShouldBeEmpty(
            "these scripts are resolved beside the binary but CAP.Desktop.csproj publishes no Content item for them, " +
            "so the feature fails in every installed build:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void DevOnlyAllowList_HasNoStaleEntries()
    {
        var resolved = Lookups.Select(lookup => lookup.ScriptPath).ToHashSet();

        DevOnlyScripts.Keys.Where(script => !resolved.Contains(script)).ShouldBeEmpty(
            "allow-listed scripts the app no longer resolves; remove them from DevOnlyScripts");
    }

    private static string DesktopProjectFile() => Path.Combine(RepositoryRoot, "CAP.Desktop", "CAP.Desktop.csproj");
}

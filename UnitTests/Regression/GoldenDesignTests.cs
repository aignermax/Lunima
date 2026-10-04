using System.Text.Json;
using Shouldly;
using Xunit;

namespace UnitTests.Regression;

/// <summary>
/// Golden-design regression gate: every example listed in
/// <c>UnitTests/Regression/golden/manifest.json</c> is loaded, re-routed, checked with DRC-lite
/// (no red routes, no overlaps/unconnected pins/bounds issues), and simulated;
/// its transmission spectrum — and any authored truth-table rows — must match
/// the pinned reference in <c>UnitTests/Regression/golden/</c> within the
/// manifest tolerance. Regenerate references by running this test with
/// <c>LUNIMA_UPDATE_GOLDEN=1</c> once and review the JSON like a snapshot.
/// Coverage starts with the Mach-Zehnder interferometer; add further examples
/// to the golden manifest as their gates are authored. (Ladder completeness —
/// every shipped .lun listed in <c>examples/examples.json</c> — is pinned by
/// <c>ExampleDesignFilesTests</c>, not duplicated here.)
/// </summary>
public class GoldenDesignTests
{
    /// <summary>Theory data: every manifest entry's example file name.</summary>
    public static IEnumerable<object[]> ManifestEntries()
    {
        foreach (var entry in GoldenManifest.Load(GoldenManifest.LocateRepositoryRoot()))
            yield return new object[] { entry.File };
    }

    /// <summary>
    /// A golden entry must point at a shipped .lun that the Home ladder also lists —
    /// otherwise the gate silently covers a phantom or non-shipped example.
    /// </summary>
    [Fact]
    public void Manifest_EntriesShipAndAreOnTheLadder()
    {
        var root = GoldenManifest.LocateRepositoryRoot();
        var listed = GoldenManifest.Load(root).Select(e => e.File).ToList();
        var shipped = Directory
            .GetFiles(GoldenManifest.ExamplesDirectory(root), "*.lun")
            .Select(Path.GetFileName)
            .ToList();
        using var ladder = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(GoldenManifest.ExamplesDirectory(root), "examples.json")));
        var ladderFiles = ladder.RootElement.GetProperty("examples").EnumerateArray()
            .Select(e => e.GetProperty("file").GetString())
            .ToList();

        foreach (var fileName in listed)
        {
            shipped.Contains(fileName).ShouldBeTrue(
                $"golden/manifest.json lists '{fileName}' but no such .lun ships — remove the stale entry.");
            ladderFiles.Contains(fileName).ShouldBeTrue(
                $"golden/manifest.json covers '{fileName}' but the Home ladder (examples/examples.json) does not list it.");
        }
    }

    /// <summary>Every manifest entry must have a pinned golden file (unless regenerating).</summary>
    [Fact]
    public void ManifestEntries_HaveGoldenReferenceFiles()
    {
        if (GoldenManifest.IsUpdateMode()) return;

        var root = GoldenManifest.LocateRepositoryRoot();
        foreach (var entry in GoldenManifest.Load(root))
        {
            var path = GoldenManifest.GoldenPath(root, entry);
            File.Exists(path).ShouldBeTrue(
                $"Example '{entry.Name}' has no golden reference — run the gate once with "
                + $"{GoldenManifest.UpdateSwitchName}=1 and review {path} like a snapshot.");
        }
    }

    /// <summary>
    /// Full gate per example: load → re-route → DRC-lite → sweep → compare
    /// against the pinned reference (or regenerate it in update mode).
    /// </summary>
    [Theory]
    [MemberData(nameof(ManifestEntries))]
    public async Task Example_PassesGoldenGate(string exampleFile)
    {
        var root = GoldenManifest.LocateRepositoryRoot();
        var entry = GoldenManifest.Load(root).Single(e => e.File == exampleFile);
        var goldenPath = GoldenManifest.GoldenPath(root, entry);

        var existing = File.Exists(goldenPath) ? GoldenReference.Read(goldenPath) : null;
        var run = await GoldenGate.RunAsync(entry, root, existing);

        run.Violations.Count.ShouldBe(
            0, "Gate violations: " + string.Join("; ", run.Violations));

        if (GoldenManifest.IsUpdateMode())
        {
            GoldenReference.Write(goldenPath, new GoldenReference
            {
                WavelengthsNm = run.WavelengthsNm,
                Transmission = run.Transmission,
                TruthTable = existing?.TruthTable ?? new List<GoldenTruthRow>(),
            });
            return;
        }

        File.Exists(goldenPath).ShouldBeTrue(
            $"No golden reference for '{entry.Name}' — run with {GoldenManifest.UpdateSwitchName}=1 once.");

        var failures = GoldenComparer.Compare(entry, GoldenReference.Read(goldenPath), run);
        failures.Count.ShouldBe(0, string.Join("; ", failures));
    }
}

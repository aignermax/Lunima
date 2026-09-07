using Shouldly;
using Xunit;

namespace UnitTests.Regression;

/// <summary>
/// Golden-design regression gate: every shipped example listed in
/// <c>examples/examples.json</c> is loaded, re-routed, checked with DRC-lite
/// (no red routes, no overlaps/unconnected pins/bounds issues), and simulated;
/// its transmission spectrum — and any authored truth-table rows — must match
/// the pinned reference in <c>UnitTests/Regression/golden/</c> within the
/// manifest tolerance. Regenerate references by running this test with
/// <c>LUNIMA_UPDATE_GOLDEN=1</c> once and review the JSON like a snapshot.
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
    /// A shipped .lun must be listed in the manifest and vice versa — otherwise
    /// the gate silently covers fewer (or phantom) examples.
    /// </summary>
    [Fact]
    public void Manifest_ListsEveryShippedExample()
    {
        var root = GoldenManifest.LocateRepositoryRoot();
        var listed = GoldenManifest.Load(root).Select(e => e.File).ToList();
        var shipped = Directory
            .GetFiles(GoldenManifest.ExamplesDirectory(root), "*.lun")
            .Select(Path.GetFileName)
            .ToList();

        foreach (var file in shipped)
        {
            listed.Contains(file).ShouldBeTrue(
                $"'{file}' ships in examples/ but is missing from examples/examples.json — "
                + "add it there, then generate its golden with "
                + $"{GoldenManifest.UpdateSwitchName}=1.");
        }
        foreach (var fileName in listed)
        {
            shipped.Contains(fileName).ShouldBeTrue(
                $"examples/examples.json lists '{fileName}' but no such .lun ships — remove the stale entry.");
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

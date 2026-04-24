using System.IO;
using System.Threading.Tasks;
using CAP_DataAccess.Import;
using Shouldly;

namespace UnitTests.Import;

/// <summary>
/// Fit-check against the real Lumerical / SiEPIC reference files shipped in
/// <c>Tools/sparam-data/</c>. The synthetic tests in
/// <c>SParameterImporterTests</c> verify the parser against hand-crafted
/// content that matches the format spec; these tests verify the same parser
/// against files actually produced by Lumerical INTERCONNECT and the
/// SiEPIC EBeam PDK.
///
/// A failure here means real users will hit the same failure — the synthetic
/// tests alone are not enough. Each failing file is documented with its
/// actual shape so a future fix has clear context.
/// </summary>
public class RealLumericalReferenceFilesTests
{
    private static readonly string DataDir = FindRepoRelative("Tools", "sparam-data");

    public static IEnumerable<object[]> BlockedSparamFiles() => new[]
    {
        new object[] { "bdc_te1550.sparam" },
        new object[] { "dc_te1550.sparam" },
        new object[] { "dc_te1550_Lc5.sparam" },
        new object[] { "y_branch.sparam" },
        new object[] { "terminator_te1550.sparam" },
        new object[] { "terminator_tm1550.sparam" },
        new object[] { "disconnected_te1550.sparam" },
        new object[] { "contra_dc.dat" },      // .dat blocked, header w/o quotes around TE
        new object[] { "dc_halfring.dat" },
        new object[] { "gc_te1310.dat" },      // blocked with port-manifest header + "mode 1" in quotes
    };

    public static IEnumerable<object[]> PackedGcFiles() => new[]
    {
        new object[] { "gc_te1550.txt" },
        new object[] { "taper_te1550.dat" },   // .dat but packed format (9 cols per row)
    };

    [Theory]
    [MemberData(nameof(BlockedSparamFiles))]
    public async Task LumericalImporter_ParsesBlockedReference(string filename)
    {
        var path = Path.Combine(DataDir, filename);
        File.Exists(path).ShouldBeTrue($"Reference file missing: {path}");

        var result = await new LumericalSParameterImporter().ImportAsync(path);

        result.ShouldNotBeNull();
        result.PortCount.ShouldBeGreaterThan(0, $"{filename}: no ports detected");
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty($"{filename}: no wavelengths parsed");

        // Sanity bound: SiEPIC sweep lives in the optical telecom range,
        // typically 1200-1700 nm. A wavelength outside [1000, 2000] means
        // unit conversion went wrong somewhere.
        foreach (var wl in result.SMatricesByWavelengthNm.Keys)
            wl.ShouldBeInRange(1000, 2000, $"{filename}: wavelength {wl} nm out of telecom range");

        // Sanity bound: S-parameter magnitudes for passive devices must be
        // in [0, ~1.01] — anything larger means we parsed magnitude in dB
        // as linear, or mis-scaled the GC packed values.
        foreach (var (_, m) in result.SMatricesByWavelengthNm)
        {
            for (int r = 0; r < result.PortCount; r++)
                for (int c = 0; c < result.PortCount; c++)
                    m[r, c].Magnitude.ShouldBeLessThanOrEqualTo(1.01, $"{filename}: |S[{r},{c}]| > 1 for a passive device");
        }
    }

    [Theory]
    [MemberData(nameof(PackedGcFiles))]
    public async Task LumericalImporter_ParsesPackedGcReference(string filename)
    {
        var path = Path.Combine(DataDir, filename);
        File.Exists(path).ShouldBeTrue($"Reference file missing: {path}");

        var result = await new LumericalSParameterImporter().ImportAsync(path);

        result.ShouldNotBeNull();
        result.PortCount.ShouldBe(2, $"{filename}: GC/taper packed format is 2-port");
        result.SMatricesByWavelengthNm.ShouldNotBeEmpty();

        foreach (var wl in result.SMatricesByWavelengthNm.Keys)
            wl.ShouldBeInRange(1000, 2000, $"{filename}: wavelength out of telecom range");

        foreach (var (_, m) in result.SMatricesByWavelengthNm)
        {
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    m[r, c].Magnitude.ShouldBeLessThanOrEqualTo(1.01, $"{filename}: |S[{r},{c}]| > 1");
        }
    }

    [Fact]
    public async Task DcTe1550_ReportsWavelengthCollisions()
    {
        // dc_te1550.sparam has 101 sampled frequencies, but frequency-space
        // sampling collapses to ~99 distinct integer-nm keys around 1550 nm.
        // The dict can only hold 99 entries, and we report the collision
        // count via Metadata so the user sees the sparsity loss.
        var result = await new LumericalSParameterImporter().ImportAsync(
            Path.Combine(DataDir, "dc_te1550.sparam"));

        result.PortCount.ShouldBe(4); // bidirectional 2+2 coupler (4 physical terminals)
        result.SMatricesByWavelengthNm.Count.ShouldBeInRange(98, 101);
        result.Metadata.ShouldContainKey("wavelengthCollisions");
        int.Parse(result.Metadata["wavelengthCollisions"]).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task YBranch_HasExpectedPortCount()
    {
        // y_branch.sparam is a 1-input / 2-output splitter = 3 ports.
        var result = await new LumericalSParameterImporter().ImportAsync(
            Path.Combine(DataDir, "y_branch.sparam"));

        result.PortCount.ShouldBe(3);
        result.SMatricesByWavelengthNm.Count.ShouldBe(51);
    }

    [Fact]
    public async Task Terminator_Is1PortDevice()
    {
        var result = await new LumericalSParameterImporter().ImportAsync(
            Path.Combine(DataDir, "terminator_te1550.sparam"));

        result.PortCount.ShouldBe(1);
        result.SMatricesByWavelengthNm.Count.ShouldBe(101);
    }

    private static string FindRepoRelative(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tools", "sparam-data")))
        {
            dir = dir.Parent;
        }
        if (dir == null) throw new InvalidOperationException("Could not locate repository root");
        return Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
    }
}

using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Regression pin for #1353: the SiEPIC round-trip interpreter selection must
/// never let a nazca-only managed env shadow a full-stack interpreter. On the
/// Windows dev machine a stale nazca-only env
/// (%LOCALAPPDATA%/Lunima/envs/fulladder995) enumerated BEFORE the full one
/// (openebl-check) and the CI PATH python; the old nazca-only probe selected
/// it, the export's klayout upgrade silently kept the stub boxes, and the
/// round trip failed with the stub topology (extra <c>heur_N</c> edge pins,
/// 2 instead of 5 restored connections, <c>heur_1_2</c>/<c>heur_2_2</c> ports
/// appearing in re-export generation 2). Linux CI has no managed-envs
/// directory and falls through to the fully provisioned PATH python — that
/// selection divergence was the whole Windows/Linux parity break.
/// </summary>
public class GdsSiepicRoundTripPythonSelectionTests
{
    private static GdsUserDesignFixture.PythonCandidateCapabilities Candidate(
        string path, bool hasNazca, bool hasSiepicUpgradeStack) =>
        new(path, hasNazca, hasSiepicUpgradeStack);

    [Fact]
    public void Selection_NazcaOnlyEnvEnumeratedFirst_StillPicksTheFullStackInterpreter()
    {
        // The exact Windows failure shape: the nazca-only env enumerates FIRST
        // (NTFS hands directory entries back alphabetically: fulladder995 < openebl-check).
        var selected = GdsUserDesignFixture.SelectRoundTripPython(new[]
        {
            Candidate(@"C:\envs\fulladder995\Scripts\python.exe", hasNazca: true, hasSiepicUpgradeStack: false),
            Candidate(@"C:\envs\openebl-check\Scripts\python.exe", hasNazca: true, hasSiepicUpgradeStack: true),
        });

        selected.ShouldBe(@"C:\envs\openebl-check\Scripts\python.exe");
    }

    [Fact]
    public void Selection_IsIndependentOfEnumerationOrder()
    {
        var nazcaOnly = Candidate("/home/ci/envs/stale/bin/python", hasNazca: true, hasSiepicUpgradeStack: false);
        var fullStack = Candidate("python3", hasNazca: true, hasSiepicUpgradeStack: true);

        GdsUserDesignFixture.SelectRoundTripPython(new[] { nazcaOnly, fullStack }).ShouldBe("python3");
        GdsUserDesignFixture.SelectRoundTripPython(new[] { fullStack, nazcaOnly }).ShouldBe("python3");
    }

    [Fact]
    public void Selection_AllCandidatesNazcaOnly_ReturnsNullSoTheScenarioSkips()
    {
        // No full-stack interpreter anywhere: the round trip must SKIP, never
        // run the upgraded-scenario expectations against stub geometry.
        var selected = GdsUserDesignFixture.SelectRoundTripPython(new[]
        {
            Candidate("python", hasNazca: true, hasSiepicUpgradeStack: false),
            Candidate("python3", hasNazca: true, hasSiepicUpgradeStack: false),
        });

        selected.ShouldBeNull();
    }

    [Fact]
    public void Selection_UpgradeStackWithoutNazca_IsNeverSelected()
    {
        // Cannot happen via the probe short-circuit, but the selection rule
        // itself must require BOTH capabilities: the exported script is a
        // nazca script first.
        var selected = GdsUserDesignFixture.SelectRoundTripPython(new[]
        {
            Candidate("python", hasNazca: false, hasSiepicUpgradeStack: true),
        });

        selected.ShouldBeNull();
    }

    [Fact]
    public void Selection_NoCandidates_ReturnsNull()
    {
        GdsUserDesignFixture.SelectRoundTripPython(
            Enumerable.Empty<GdsUserDesignFixture.PythonCandidateCapabilities>()).ShouldBeNull();
    }
}

using CAP_DataAccess.Import.Gds;
using Shouldly;

namespace UnitTests.Import.Gds;

/// <summary>Real-reader hierarchy fixtures; coordinates are micrometers in Y-down app space.</summary>
internal static class GdsHierarchyImporterFixture
{
    internal const double Tolerance = 1e-6;

    internal static DetectedPin Pin(string name, double x, double y, double angle) =>
        new() { Name = name, XUm = x, YUm = y, AngleDegrees = angle, Source = DetectedPinSource.Label };

    internal static async Task<GdsLibrary> ReadLibraryAsync(byte[] gds) =>
        await new GdsReader().ReadAsync(new MemoryStream(gds));

    /// <summary>The PdkLoader rule: pins within [0, W] × [0, H] (±1 µm tolerance).</summary>
    internal static void AssertPinsWithinDraftBounds(GdsCellDraft draft)
    {
        foreach (var pin in draft.Pins)
        {
            pin.XUm.ShouldBeGreaterThanOrEqualTo(-1.0);
            pin.XUm.ShouldBeLessThanOrEqualTo(draft.WidthUm + 1.0);
            pin.YUm.ShouldBeGreaterThanOrEqualTo(-1.0);
            pin.YUm.ShouldBeLessThanOrEqualTo(draft.HeightUm + 1.0);
        }
    }

    /// <summary>
    /// 10×4 µm cell, built like a real gdsfactory waveguide: a 0.5 µm core
    /// stripe (y ∈ [1.75, 2.25]) on the waveguide layer (1,0), an extent
    /// rectangle on the non-waveguide layer (111,0) — it sizes the bbox
    /// without firing the edge heuristic — and in/out port labels on (1,10).
    /// </summary>
    internal static GdsTestWriter WaveguideCell(this GdsTestWriter writer, string name, int inY = 2000, int outY = 2000) =>
        writer
            .BeginCell(name)
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "in", 0, inY)
                .Text(1, 10, "out", 10000, outY)
            .EndCell();

    /// <summary>
    /// Same geometry as <see cref="WaveguideCell"/> but without port labels, so
    /// the edge heuristic is the only source of pins.
    /// </summary>
    internal static GdsTestWriter UnlabeledWaveguideCell(this GdsTestWriter writer, string name) =>
        writer
            .BeginCell(name)
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .EndCell();
}

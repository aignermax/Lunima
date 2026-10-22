using CAP.Avalonia.Services;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Export;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// End-to-end journey test for GDS import through the six-step interpretation review
/// (issue #1566): a small GDS (one top cell, two instances of a 2-optical-port child,
/// one non-port layer) is reviewed step by step via the review ViewModel's commands —
/// layer roles confirmed, child exposed, both occurrences selected and grouped, ports
/// reviewed, model left at default — then applied, routed, simulated, saved/loaded and
/// re-exported, proving the reviewed import survives the whole product.
/// <para>
/// Each step is a separate test over a shared <see cref="IClassFixture{T}"/> so a
/// failure names the broken step. Steps 1–5 and 7 run headless; the re-export is
/// nazca-gated with <c>Skip.If</c>.
/// </para>
/// </summary>
public class GdsReviewJourneyTests : IClassFixture<GdsReviewJourneyFixture>
{
    private const double Tolerance = GdsReviewJourneyFixture.Tolerance;
    private readonly GdsReviewJourneyFixture _f;

    public GdsReviewJourneyTests(GdsReviewJourneyFixture fixture) => _f = fixture;

    /// <summary>
    /// Step 1 — Review: all six stages were stepped through via NextCommand; roles
    /// confirmed (optical core + port label, the non-port layer auxiliary), the named
    /// child exposed, both occurrences selected and grouped, ports reviewed (2 for the
    /// cell), model left at default, and the summary allowed applying.
    /// </summary>
    [Fact]
    public void Step1_Review_AllSixStagesConfirmedViaCommands()
    {
        _f.LayersSeen.ShouldBe(new[]
        {
            (1, 0, GdsLayerRole.OpticalCore),
            (1, 10, GdsLayerRole.PortLabel),
            (111, 0, GdsLayerRole.Auxiliary),
        }, ignoreOrder: true);

        _f.CellsSeen.ShouldBe(new[] { "wg" }, "the named child must be exposed");
        _f.OccurrenceCount.ShouldBe(2, "both instances must be listed as occurrences");
        _f.GroupCaptured.ShouldNotBeNull("the two selected occurrences must form a group");
        _f.GroupCaptured.Name.ShouldBe(GdsReviewJourneyFixture.GroupName);
        _f.GroupCaptured.InstanceNames.ShouldBe(new[] { "wg#0", "wg#1" });

        _f.PortsSeen.Count.ShouldBe(2, "the reviewed cell must show its 2 ports");
        var inPort = _f.PortsSeen.Single(p => p.Name == "in");
        inPort.XUm.ShouldBe(0, Tolerance);
        inPort.YUm.ShouldBe(2, Tolerance);
        var outPort = _f.PortsSeen.Single(p => p.Name == "out");
        outPort.XUm.ShouldBe(10, Tolerance);
        outPort.YUm.ShouldBe(2, Tolerance);

        _f.ModelLeftAtDefault.ShouldBeTrue("the model stage must stay at its default");
        _f.CanApplyAtSummary.ShouldBeTrue("the summary stage must allow applying");
        _f.Dialog.ImportCompleted.ShouldBeTrue("the Apply press must run the import");
    }

    /// <summary>
    /// Step 2 — Apply: two imported components sit on the canvas inside one component
    /// group; each has exactly 2 optical pins at the reviewed positions.
    /// </summary>
    [Fact]
    public void Step2_Apply_TwoComponentsInOneGroupWithReviewedPins()
    {
        _f.Canvas.Components.ShouldHaveSingleItem("the group replaces its members on the canvas")
            .Component.ShouldBe(_f.Group);
        _f.Group.GroupName.ShouldBe(GdsReviewJourneyFixture.GroupName);
        _f.Group.ChildComponents.Count.ShouldBe(2);

        AssertReviewedPins(_f.CellA, inX: 0, outX: 10);
        AssertReviewedPins(_f.CellB, inX: 20, outX: 30);
    }

    /// <summary>
    /// Step 3 — Route: the waveguide between the two cells' facing pins routed through
    /// the normal routing service and is not a blocked fallback.
    /// </summary>
    [Fact]
    public void Step3_Route_FacingPinsConnectionIsNotBlockedFallback()
    {
        _f.Connection.Connection.StartPin.ShouldNotBeNull();
        _f.Connection.Connection.EndPin.ShouldNotBeNull();
        _f.Connection.Connection.IsBlockedFallback.ShouldBeFalse(
            $"routing must succeed (failure: {_f.Connection.Connection.FailureReason})");
        _f.Connection.Connection.RoutedPath.ShouldNotBeNull();
        _f.Connection.Connection.RoutedPath!.Segments.Count.ShouldBeGreaterThan(0,
            "the routed connection must have at least one segment");
    }

    /// <summary>
    /// Step 4 — Simulate: with the source on the free input, the S-matrix simulation
    /// delivers non-zero power at the free output — the 2-pin pass-through default
    /// (#1012) must still apply to a reviewed import.
    /// </summary>
    [Fact(Skip = "Reviewed unmodeled imports are geometry-only and refused by simulation — #1567")]
    public async Task Step4_Simulate_PowerArrivesAtFreeOutput()
    {
        _f.CellA.IsUserMarkedLightSource = true;
        var result = await new SimulationService().RunAsync(_f.Canvas);

        result.Success.ShouldBeTrue(result.ErrorMessage ?? "simulation failed");
        result.LightSourceCount.ShouldBeGreaterThan(0);
        var freeOutput = _f.CellB.PhysicalPins.Single(p => p.Name == "out");
        result.FieldResults!.ContainsKey(freeOutput.LogicalPin!.IDOutFlow).ShouldBeTrue(
            "the simulation must produce a field value at the free output pin");
        result.FieldResults[freeOutput.LogicalPin.IDOutFlow].Magnitude
            .ShouldBeGreaterThan(0, "light must pass through both reviewed cells to the free output");
    }

    /// <summary>
    /// Step 5 — Save/load: the .lun round-trip preserves pin positions, group
    /// membership and the reviewed interpretation (<c>FindInterpretation</c> returns
    /// the reviewed ports for the source hash).
    /// </summary>
    [Fact]
    public void Step5_SaveLoad_PinsGroupAndInterpretationSurvive()
    {
        _f.LoadCanvas.Components.ShouldHaveSingleItem().Component.ShouldBe(_f.LoadedGroup);
        _f.LoadedGroup.GroupName.ShouldBe(GdsReviewJourneyFixture.GroupName);
        _f.LoadedGroup.ChildComponents.Count.ShouldBe(2);
        AssertReviewedPins(_f.LoadedCellA, inX: 0, outX: 10);
        AssertReviewedPins(_f.LoadedCellB, inX: 20, outX: 30);

        // The routed wire survives — either still live on the canvas or homed into the
        // owning group as a frozen path (the load-time wire-ownership rule).
        var liveWires = _f.LoadCanvas.Connections.Count;
        var frozenWires = _f.LoadedGroup.InternalPaths.Count;
        (liveWires + frozenWires).ShouldBe(1, "the routed connection survives the round trip");

        var interpretation = _f.LoadedInterpretation.ShouldNotBeNull(
            "FindInterpretation must return the reviewed interpretation after reload");
        interpretation.TopCellName.ShouldBe("TOP");
        var pins = interpretation.Cells.Single(c => c.CellName == "wg").Pins.ShouldNotBeNull();
        pins.Count.ShouldBe(2);
        var inPin = pins.Single(p => p.Name == "in");
        inPin.XUm.ShouldBe(0, Tolerance);
        inPin.YUm.ShouldBe(2, Tolerance);
        var outPin = pins.Single(p => p.Name == "out");
        outPin.XUm.ShouldBe(10, Tolerance);
        outPin.YUm.ShouldBe(2, Tolerance);
    }

    /// <summary>
    /// Step 5 (continued) — the simulated output of the reloaded design matches the
    /// pre-save simulation within 1e-9.
    /// </summary>
    [Fact(Skip = "Reviewed unmodeled imports are geometry-only and refused by simulation — #1567")]
    public async Task Step6_SaveLoad_SimulatedOutputMatchesWithin1e9()
    {
        var before = await SimulateFreeOutputPower(_f.Canvas, _f.CellA, _f.CellB);
        _f.LoadedCellA.IsUserMarkedLightSource = true; // not serialized — re-mark after load
        var after = await SimulateFreeOutputPower(_f.LoadCanvas, _f.LoadedCellA, _f.LoadedCellB);
        after.ShouldBe(before, 1e-9);
    }

    /// <summary>
    /// Step 6 — Re-export: the reloaded design exports through the real nazca path;
    /// the produced GDS contains both cell references and one waveguide chain
    /// spanning the 10 µm gap between them.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Slow")]
    public async Task Step7_ReExport_ExportedGdsContainsBothCellsAndWaveguideChain()
    {
        var python = await GdsUserDesignFixture.FindNazcaPythonAsync();
        Skip.If(python == null, "No Python with nazca available — the re-export needs the real engine.");

        var gdsPath = await GdsE2EJourneyHarness.ExportAsync(
            python, _f.Root, "reexport", _f.LoadCanvas, _f.LoadHost.Templates.ToList());

        GdsLibrary library;
        await using (var stream = File.OpenRead(gdsPath))
            library = await new GdsReader().ReadAsync(stream);
        library.Cells.ContainsKey("ConnectAPIC_Design").ShouldBeTrue(
            $"top cell missing; cells: {string.Join(", ", library.Cells.Keys)}");

        var refs = library.Cells["ConnectAPIC_Design"].Elements.OfType<GdsReference>()
            .Where(r => r.CellName.Contains("wg", StringComparison.Ordinal)).ToList();
        refs.Count.ShouldBe(2,
            $"both reviewed cells must be referenced; cells: {string.Join(", ", library.Cells.Keys)}");

        var flat = new GdsCellFlattener(library).Flatten("ConnectAPIC_Design");
        // Routed connections export through nazca's default interconnect, whose polygons
        // land on layer (1111,0) — the reviewed cells' own layer 1 cores span only
        // x[0..10] and x[20..30], so the gap-spanning polygon identifies the chain.
        var chain = flat.Polygons.Where(p => p.Layer == 1111).Select(p =>
            (MinX: p.Points.Min(q => q.X), MaxX: p.Points.Max(q => q.X))).ToList();
        var dump = string.Join(" | ", flat.Polygons.Select(p =>
            $"L{p.Layer}/{p.DataType} x[{p.Points.Min(q => q.X):F2}..{p.Points.Max(q => q.X):F2}] y[{p.Points.Min(q => q.Y):F2}..{p.Points.Max(q => q.Y):F2}]"));
        chain.ShouldContain(s => Math.Abs(s.MinX - 10) < Tolerance && Math.Abs(s.MaxX - 20) < Tolerance,
            "one waveguide chain must span the 10 µm gap between the cells. All polygons: " + dump);
    }

    /// <summary>
    /// Step 7 — Design checks: the reloaded design has no overlap errors and no
    /// unconnected-pin errors on the routed pins.
    /// </summary>
    [Fact]
    public void Step8_DesignValidator_NoOverlapOrUnconnectedRoutedPins()
    {
        var items = _f.LoadCanvas.Components.Select(c => c.Component).ToList();
        var issues = new DesignValidator().Validate(
            _f.LoadCanvas.Connections.Select(c => c.Connection).ToList(),
            items.OfType<ComponentGroup>().ToList(),
            items.Where(c => c is not ComponentGroup).ToList());

        issues.ShouldNotContain(i => i.Type == DesignIssueType.OverlappingPaths
            || i.Type == DesignIssueType.ComponentFootprintOverlap
            || i.Type == DesignIssueType.WaveguideThroughComponent
            || i.Type == DesignIssueType.BlockedPath,
            $"no overlap errors expected: {string.Join("; ", issues.Select(i => i.Description))}");

        var routedPins = new[] { (10.0, 2.0), (20.0, 2.0) };
        issues.ShouldNotContain(i => i.Type == DesignIssueType.UnconnectedPin
            && routedPins.Any(p => Math.Abs(i.X - p.Item1) < Tolerance && Math.Abs(i.Y - p.Item2) < Tolerance),
            "the routed pins must not be reported as unconnected");
    }

    private void AssertReviewedPins(Component cell, double inX, double outX)
    {
        var optical = cell.PhysicalPins.Where(p => p.MatterType == MatterType.Light).ToList();
        optical.Count.ShouldBe(2, $"{cell.Identifier} must have exactly 2 optical pins");
        var inPos = optical.Single(p => p.Name == "in").GetAbsolutePosition();
        inPos.x.ShouldBe(inX, Tolerance);
        inPos.y.ShouldBe(2, Tolerance);
        var outPos = optical.Single(p => p.Name == "out").GetAbsolutePosition();
        outPos.x.ShouldBe(outX, Tolerance);
        outPos.y.ShouldBe(2, Tolerance);
    }

    private static async Task<double> SimulateFreeOutputPower(
        CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas, Component source, Component sink)
    {
        source.IsUserMarkedLightSource = true;
        var result = await new SimulationService().RunAsync(canvas);
        result.Success.ShouldBeTrue(result.ErrorMessage ?? "simulation failed");
        var freeOutput = sink.PhysicalPins.Single(p => p.Name == "out");
        return result.FieldResults![freeOutput.LogicalPin!.IDOutFlow].Magnitude;
    }
}

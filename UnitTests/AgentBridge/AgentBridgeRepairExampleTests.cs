using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using static UnitTests.AgentBridge.RepairTestBench;

namespace UnitTests.AgentBridge;

/// <summary>
/// The repair tools on an earlier bake of the <c>Logic Gate Counter 2-bit</c> example with one
/// sealed wire (<see cref="RepairTestBench.SealedCounterFixture"/>, loaded through the
/// production load path, crossing undo journal attached): cut candidates, crossing insertion
/// and undo, crossing slides, segment shifts, the suggest/apply loop on its blocked wire, and a
/// save round trip. Full routing passes on a multi-gate design make these heavyweight.
/// </summary>
[Trait("Category", "Slow")]
public class AgentBridgeRepairExampleTests
{
    [Fact]
    public async Task ListCutCandidates_FindsFittingSpots_IncludingOneForTheBlockedWire()
    {
        var bench = await LoadAsync(SealedCounterFixture);

        var all = Body(await new ListCutCandidatesTool(bench.Session).ExecuteAsync(Args("""{"fits_only":true}""")));
        var forBlocked = Body(await new ListCutCandidatesTool(bench.Session).ExecuteAsync(
            Args($$"""{"connection_id":"{{bench.FirstBlockedId()}}"}""")));

        all.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);
        all.GetProperty("spots").EnumerateArray().ShouldAllBe(s => s.GetProperty("fits").GetBoolean());
        var spots = forBlocked.GetProperty("spots").EnumerateArray().ToList();
        spots.ShouldContain(s => s.GetProperty("fits").GetBoolean()
            && s.GetProperty("through_connection_id").GetString() == bench.FirstBlockedId());
    }

    [Fact]
    public async Task InsertCrossing_ThenUndo_RestoresComponentsAndConnections()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var (components, connections) = (bench.Canvas.Components.Count, bench.Canvas.Connections.Count);
        var spot = Body(await new ListCutCandidatesTool(bench.Session).ExecuteAsync(Args("""{"fits_only":true,"limit":500}""")))
            .GetProperty("spots").EnumerateArray().First(s => s.GetProperty("kind").GetString() == "pin_guide");

        var inserted = Body(await new InsertCrossingTool(bench.Session).ExecuteAsync(Args(
            $$"""{"connection_id":"{{spot.GetProperty("cut_connection_id").GetString()}}","x":{{Num(spot, "x")}},"y":{{Num(spot, "y")}}}""")));

        inserted.GetProperty("crossing").GetString().ShouldNotBeNullOrEmpty();
        bench.Canvas.Components.Count.ShouldBe(components + 1);
        bench.Commands.UndoCount.ShouldBe(1);

        Body(await new UndoTool(bench.Session).ExecuteAsync(Args("{}")));
        bench.Canvas.Components.Count.ShouldBe(components);
        bench.Canvas.Connections.Count.ShouldBe(connections);
    }

    [Fact]
    public async Task InsertCrossing_WithThroughWire_DocksTheBlockedWireOnTheCrossing()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var blocked = bench.FirstBlockedId();
        var spot = Body(await new ListCutCandidatesTool(bench.Session).ExecuteAsync(
                Args($$"""{"connection_id":"{{blocked}}","fits_only":true}""")))
            .GetProperty("spots").EnumerateArray()
            .First(s => s.GetProperty("through_connection_id").GetString() == blocked);

        var result = Body(await new InsertCrossingTool(bench.Session).ExecuteAsync(Args(
            $$"""{"connection_id":"{{spot.GetProperty("cut_connection_id").GetString()}}","x":{{Num(spot, "x")}},"y":{{Num(spot, "y")}},"through_connection_id":"{{blocked}}"}""")));

        result.GetProperty("routed_through").GetBoolean().ShouldBeTrue();
        bench.Session.FindConnection(blocked).ShouldBeNull("the blocked wire was replaced by two pieces docked on the crossing");

        Body(await new UndoTool(bench.Session).ExecuteAsync(Args("{}")));
        bench.Session.FindConnection(blocked).ShouldNotBeNull("one undo restores the original wire");
    }

    [Fact]
    public async Task MoveCrossingAlongAxis_MovesAValidStep_AndExplainsARefusal()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var crossing = bench.Canvas.Components.First(c => CrossingComponentCatalog.IsCrossing(c.Component));
        var (x, y) = (crossing.X, crossing.Y);
        var step = new[] { ("horizontal", 10.0), ("horizontal", -10.0), ("vertical", 10.0), ("vertical", -10.0) }
            .First(s => CAP.Avalonia.Commands.MoveCrossingAlongAxisCommand.Validate(bench.Canvas, crossing.Component,
                s.Item1 == "horizontal" ? CAP_Core.Routing.CrossingInsertion.CrossingMove.CrossingAxis.Horizontal
                                        : CAP_Core.Routing.CrossingInsertion.CrossingMove.CrossingAxis.Vertical, s.Item2) == null);

        Body(await new MoveCrossingAlongAxisTool(bench.Session).ExecuteAsync(Args(
            $$"""{"crossing_id":"{{crossing.Component.Identifier}}","axis":"{{step.Item1}}","delta_um":{{step.Item2}}}""")));
        (Math.Abs(crossing.X - x) + Math.Abs(crossing.Y - y)).ShouldBe(10, 1e-6);

        Body(await new UndoTool(bench.Session).ExecuteAsync(Args("{}")));
        (crossing.X, crossing.Y).ShouldBe((x, y));

        var refused = await new MoveCrossingAlongAxisTool(bench.Session).ExecuteAsync(Args(
            $$"""{"crossing_id":"{{crossing.Component.Identifier}}","axis":"horizontal","delta_um":0}"""));
        refused.IsError.ShouldBeTrue();
        refused.Content[0].Text.ShouldContain("Cannot move the crossing");
    }

    [Fact]
    public async Task ShiftSegment_RecordsTheOffset_AndUndoRestoresIt()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var (wire, handle) = bench.Canvas.Connections
            .Where(c => !c.Connection.IsBlockedFallback)
            .SelectMany(c => SegmentShiftGeometry.GetHandles(c.Connection.GetPathSegments()).Select(h => (c, h)))
            .First();

        var body = Body(await new ShiftSegmentTool(bench.Session).ExecuteAsync(Args(
            $$"""{"connection_id":"{{wire.Connection.Id}}","straight_index":{{handle.StraightIndex}},"offset_um":2}""")));

        body.GetProperty("routing_complete").GetBoolean().ShouldBeTrue();
        ShiftSegmentTool.CurrentOffset(wire.Connection, handle.StraightIndex).ShouldBe(2);
        Body(await new UndoTool(bench.Session).ExecuteAsync(Args("{}")));
        ShiftSegmentTool.CurrentOffset(wire.Connection, handle.StraightIndex).ShouldBe(0);
    }

    [Fact]
    public async Task SuggestFixes_RanksTriedCandidates_AndApplyingTheBestMatchesItsTrial()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var blocked = bench.FirstBlockedId();
        var before = bench.BlockedCount();

        // One trial keeps the test well inside the CI runner's 5-minute inactivity limit: the
        // neighbour-reorder trial alone takes over a minute on a shared runner.
        var body = Body(await new SuggestFixesTool(bench.Session).ExecuteAsync(
            Args($$"""{"connection_id":"{{blocked}}","time_budget_s":120,"max_trials":1}""")));

        body.GetProperty("sandbox_problem").ValueKind.ShouldBe(JsonValueKind.Null, body.ToString());
        bench.BlockedCount().ShouldBe(before, "the sandbox leaves the design as it found it");
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        suggestions.ShouldNotBeEmpty(body.ToString());
        suggestions.Select(s => s.GetProperty("blocked_after").GetInt32()).ShouldBeInOrder();
        var best = suggestions[0];
        best.GetProperty("explanation").GetString().ShouldNotBeNullOrEmpty();

        body.GetProperty("summary").GetString().ShouldNotBeNullOrEmpty();

        var undoBefore = bench.Commands.UndoCount;
        var result = await new ApplySuggestionTool(bench.Session).ExecuteAsync(Args("""{"id":"s1"}"""));
        if (best.GetProperty("blocked_after").GetInt32() > before)
        {
            result.IsError.ShouldBeTrue("a suggestion whose trial made things worse is not applied by default");
            result.Content[0].Text.ShouldContain("allow_worse");
            bench.BlockedCount().ShouldBe(before);
            return;
        }
        var applied = Body(result);
        applied.GetProperty("blocked_after").GetInt32().ShouldBeLessThanOrEqualTo(before + 1, applied.ToString());
        (bench.Commands.UndoCount - undoBefore).ShouldBe(applied.GetProperty("undo_steps").GetInt32());
    }

    [Fact]
    public async Task SaveDesign_RoundTripsThroughTheLoader_AndRefusesSilentOverwrite()
    {
        var bench = await LoadAsync(SealedCounterFixture);
        var directory = Path.Combine(Path.GetTempPath(), "lunima-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "repaired.lun");
            var saved = Body(await new SaveDesignTool(bench.Session).ExecuteAsync(
                Args(JsonSerializer.Serialize(new { path }))));
            saved.GetProperty("saved").GetString().ShouldBe(path);

            var (canvas, _, _) = await MziFringeAnalysis.LoadDesignFromPath(path);
            canvas.Components.Count.ShouldBe(bench.Canvas.Components.Count);
            canvas.Connections.Count.ShouldBe(bench.Canvas.Connections.Count);

            var again = await new SaveDesignTool(bench.Session).ExecuteAsync(Args(JsonSerializer.Serialize(new { path })));
            again.IsError.ShouldBeTrue("the file exists outside the examples folder");
            Body(await new SaveDesignTool(bench.Session).ExecuteAsync(
                Args(JsonSerializer.Serialize(new { path, overwrite = true }))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Geometry_OfTheBlockedWire_ListsItsPinNeighbour()
    {
        var bench = await LoadAsync(SealedCounterFixture);

        var body = Body(await new GetConnectionGeometryTool(bench.Session).ExecuteAsync(
            Args($$"""{"id":"{{bench.FirstBlockedId()}}","radius_um":12}""")));

        body.GetProperty("blocked").GetBoolean().ShouldBeTrue();
        body.GetProperty("nearby_wires").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private static string Num(JsonElement element, string name) =>
        element.GetProperty(name).GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
}

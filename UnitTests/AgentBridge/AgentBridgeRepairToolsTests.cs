using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Geometry;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.Services.AgentBridge.Tools.Session;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.CrossingInsertion.CrossingMove;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// Fast checks of the repair tools on a small headless canvas (two waveguides, one wire) and of
/// their pure helpers. The end-to-end repair flow on a shipped example is in
/// <see cref="AgentBridgeRepairExampleTests"/>.
/// </summary>
public class AgentBridgeRepairToolsTests
{
    private readonly DesignCanvasViewModel _canvas = new();
    private readonly AgentDesignSession _session;
    private readonly string _wireId;

    public AgentBridgeRepairToolsTests()
    {
        _canvas.InitializeAStarRouting(0, 0, 3000, 3000);
        var a = AddWaveguide("wgA", 100, 100);
        var b = AddWaveguide("wgB", 1000, 400);
        var vm = _canvas.ConnectPins(a.Component.PhysicalPins.Single(p => p.Name == "out"),
            b.Component.PhysicalPins.Single(p => p.Name == "in"));
        _wireId = vm!.Connection.Id.ToString();
        _session = new AgentDesignSession(_canvas, new CommandManager(), () => null, new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
    }

    private ComponentViewModel AddWaveguide(string id, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = id;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return _canvas.AddComponent(component);
    }

    private static JsonElement Args(string json) => RepairTestBench.Args(json);

    [Fact]
    public async Task Geometry_ReportsPinsSegmentsAndNearbyComponents()
    {
        await _canvas.RecalculateRoutesAsync();

        var body = RepairTestBench.Body(await new GetConnectionGeometryTool(_session).ExecuteAsync(
            Args($$"""{"id":"{{_wireId}}","radius_um":5}""")));

        body.GetProperty("start_pin").GetProperty("pin").GetString().ShouldBe("wgA.out");
        body.GetProperty("end_pin").GetProperty("angle_degrees").GetDouble().ShouldBe(180);
        var segments = body.GetProperty("segments").EnumerateArray().ToList();
        segments.ShouldNotBeEmpty();
        segments.Where(s => s.GetProperty("type").GetString() == "straight")
            .Select(s => s.GetProperty("straight_index").GetInt32()).ShouldBe(
                Enumerable.Range(0, segments.Count(s => s.GetProperty("type").GetString() == "straight")));
        body.GetProperty("nearby_components").EnumerateArray().Select(c => c.GetProperty("id").GetString())
            .ShouldBe(new[] { "wgA", "wgB" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Geometry_UnknownId_IsToolError()
    {
        (await new GetConnectionGeometryTool(_session).ExecuteAsync(Args("""{"id":"nope"}"""))).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task ShiftSegment_NonShiftableStraight_IsRefusedWithTheShiftableList()
    {
        await _canvas.RecalculateRoutesAsync();

        var result = await new ShiftSegmentTool(_session).ExecuteAsync(
            Args($$"""{"connection_id":"{{_wireId}}","straight_index":99,"offset_um":5}"""));

        result.IsError.ShouldBeTrue();
        result.Content[0].Text.ShouldContain("not shiftable");
        _session.Commands.UndoCount.ShouldBe(0);
    }

    [Fact]
    public async Task ListCutCandidates_WithoutCrossingCell_IsToolError()
    {
        var result = await new ListCutCandidatesTool(_session).ExecuteAsync(Args("{}"));

        result.IsError.ShouldBeTrue("the session has no PDK library, so no crossing can be inserted");
    }

    [Fact]
    public async Task MoveCrossing_OnANonCrossing_IsToolError()
    {
        var result = await new MoveCrossingAlongAxisTool(_session).ExecuteAsync(
            Args("""{"crossing_id":"wgA","axis":"horizontal","delta_um":10}"""));

        result.IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("horizontal", CrossingAxis.Horizontal)]
    [InlineData("X", CrossingAxis.Horizontal)]
    [InlineData(" Vertical ", CrossingAxis.Vertical)]
    public void TryParseAxis_AcceptsNamesAndLetters(string text, CrossingAxis expected)
    {
        MoveCrossingAlongAxisTool.TryParseAxis(text, out var axis).ShouldBeTrue();
        axis.ShouldBe(expected);
        MoveCrossingAlongAxisTool.TryParseAxis("diagonal", out _).ShouldBeFalse();
    }

    [Fact]
    public void SaveDesign_ResolvesBareNamesIntoTheExamplesFolder_AndNeedsAPathForUntitledDesigns()
    {
        var tool = new SaveDesignTool(_session);
        var examples = Path.Combine(Path.GetTempPath(), "examples");

        tool.ResolveTarget("Counter", null, examples, out _).ShouldBe(Path.Combine(examples, "Counter.lun"));
        tool.ResolveTarget(null, "C:/designs/a.lun", examples, out _).ShouldBe("C:/designs/a.lun");
        tool.ResolveTarget(null, null, examples, out var error).ShouldBeNull();
        error.ShouldNotBeNullOrEmpty();
        tool.ResolveTarget(Path.Combine("sub", "x.lun"), null, examples, out _).ShouldBeNull("only bare names go to the examples folder");
    }

    [Fact]
    public void Ranking_PrefersFewerBlocked_ThenFewerCrossings_ThenSmallerChange_AndDropsInapplicable()
    {
        RepairCandidate Candidate(string name, double change) =>
            new("nudge_component", name, change, new { }, new NudgeRepair(Guid.Empty, change, 0));
        var outcomes = new[]
        {
            new TrialOutcome(Candidate("worse", 1), true, null, 3, 0, false, 1),
            new TrialOutcome(Candidate("crossing", 0), true, null, 0, 1, true, 1),
            new TrialOutcome(Candidate("big move", 20), true, null, 0, 0, true, 1),
            new TrialOutcome(Candidate("small move", 10), true, null, 0, 0, true, 1),
            new TrialOutcome(Candidate("n/a", 0), false, "refused", -1, 0, false, 1),
        };

        var ranked = RepairSuggestionSet.Rank(outcomes);

        ranked.Select(s => s.Outcome.Candidate.Description).ShouldBe(new[] { "small move", "big move", "crossing", "worse" });
        ranked.Select(s => s.Id).ShouldBe(new[] { "s1", "s2", "s3", "s4" });
    }

    [Fact]
    public async Task ApplySuggestion_WithoutSuggestions_IsToolError()
    {
        (await new ApplySuggestionTool(_session).ExecuteAsync(Args("""{"id":"s1"}"""))).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task SuggestFixes_OnARoutedWire_IsToolError()
    {
        await _canvas.RecalculateRoutesAsync();

        var result = await new SuggestFixesTool(_session).ExecuteAsync(Args($$"""{"connection_id":"{{_wireId}}"}"""));

        result.IsError.ShouldBeTrue();
        result.Content[0].Text.ShouldContain("not blocked");
    }

    [Fact]
    public void CompositeCommand_UndoesInReverseOrder()
    {
        var log = new List<string>();
        var composite = new CompositeCommand("both", new IUndoableCommand[] { new Step("a", log), new Step("b", log) });

        composite.Execute();
        composite.Undo();

        log.ShouldBe(new[] { "do a", "do b", "undo b", "undo a" });
    }

    private sealed class Step : IUndoableCommand
    {
        private readonly string _name;
        private readonly List<string> _log;

        public Step(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public string Description => _name;
        public void Execute() => _log.Add("do " + _name);
        public void Undo() => _log.Add("undo " + _name);
    }
}

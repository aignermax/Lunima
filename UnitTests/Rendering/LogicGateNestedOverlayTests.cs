using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Rendering;

/// <summary>
/// Placement tests for the canvas badges and register markers of hierarchical designs
/// (issue #1398): both renderers resolve their groups through the network's
/// hierarchical gate ids (<c>CELL0/REG00</c>) instead of only the top-level
/// <see cref="ComponentGroup.GroupName"/>, so the RAM 2x4's eight register bits —
/// nested inside the two instanced word cells — carry their live 0/1 badge and their
/// "R" marker on their own absolute group bounds, and a flat design (the full adder)
/// computes exactly the badge and marker set the pre-hierarchical renderer drew.
/// Exercises the renderer-computed sets (<see cref="LogicGateStateBadgeRenderer"/> and
/// <see cref="LogicGateRegisterMarkerRenderer"/>), never pixel samples.
/// </summary>
public class LogicGateNestedOverlayTests
    : IClassFixture<LogicGateRam2x4ExampleTests.Ram2x4Fixture>
{
    private const int WordCount = 2;
    private const int BitCount = 4;

    private static readonly string[] NestedRegisterIds = Enumerable
        .Range(0, WordCount * BitCount)
        .Select(i => $"CELL{i / BitCount}/REG{i / BitCount}{i % BitCount}")
        .ToArray();

    private readonly LogicGateRam2x4ExampleTests.Ram2x4Fixture _fixture;

    /// <summary>Attaches the shared loaded RAM 2x4 example and its assembled network.</summary>
    public LogicGateNestedOverlayTests(LogicGateRam2x4ExampleTests.Ram2x4Fixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Ram2x4_BadgePlacements_CoverEveryGateOutput_IncludingNestedRegisters()
    {
        var canvas = _fixture.Canvas;
        await BuildPanelNetwork(canvas);

        var placements = LogicGateStateBadgeRenderer.ComputePlacements(canvas);

        placements.SelectMany(p => p.Badges).Select(b => b.GroupName).Distinct()
            .ShouldBe(_fixture.Network.Gates.Keys, ignoreOrder: true,
                customMessage: "every network gate — nested registers included — carries its badges");
        placements.SelectMany(p => p.Badges).Count().ShouldBe(canvas.LogicGateStates.Badges.Count,
            "the renderer drops no emitted badge — before #1398 every nested badge fell away");

        // One placement per gate output tap: named taps (Q0–Q3) badge by their signal
        // name instead of anonymously (issue #1067), so filter the named input chips
        // (issue #1051) out by their signal names, which are network inputs, not taps.
        var tapPins = _fixture.Network.OutputTaps.Values
            .Select(tap => (tap.GateId, tap.PinName)).ToList();
        var outputBadges = placements.SelectMany(p => p.Badges)
            .Where(b => !b.HasSignalName || _fixture.Network.OutputPinNames.Contains(b.SignalName))
            .Select(b => (b.GroupName, b.PinName)).ToList();
        outputBadges.ShouldBe(tapPins, ignoreOrder: true,
            customMessage: "exactly one badge placement per gate output tap of the assembled network");

        foreach (var registerId in NestedRegisterIds)
        {
            var placement = placements.Single(p => p.Badges.Any(b => b.GroupName == registerId));
            var nestedGroup = FindGroupByPath(canvas, registerId);
            placement.GroupBounds.ShouldBe(ComponentGroupRenderer.CalculateGroupBounds(nestedGroup),
                $"the badges of '{registerId}' sit on the nested group's own absolute bounds");
        }
    }

    [Fact]
    public async Task Ram2x4_RegisterMarkers_CoverAllEightNestedRegisterBits()
    {
        var canvas = _fixture.Canvas;
        await BuildPanelNetwork(canvas);

        var markers = LogicGateRegisterMarkerRenderer.ComputeMarkers(canvas);

        markers.Select(m => m.GateId).ShouldBe(NestedRegisterIds, ignoreOrder: true,
            customMessage: "the eight register bits nested inside the cell instances carry the 'R' marker");
        foreach (var (gateId, bounds) in markers)
        {
            bounds.ShouldBe(ComponentGroupRenderer.CalculateGroupBounds(FindGroupByPath(canvas, gateId)),
                $"the marker of '{gateId}' sits on the nested group's own absolute bounds");
        }
    }

    [Fact]
    public async Task FullAdder_TopLevelDesign_BadgeAndMarkerSetsStayUnchanged()
    {
        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Full Adder.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
        await BuildPanelNetwork(canvas);

        var placements = LogicGateStateBadgeRenderer.ComputePlacements(canvas);

        // The pre-hierarchical renderer matched badges by the top-level GroupName and
        // drew them on the top-level group's bounds — pin that exact set.
        var expected = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
            .Where(g => g.TruthTablePinAssignment != null)
            .ToDictionary(g => g.GroupName, g => ComponentGroupRenderer.CalculateGroupBounds(g));
        expected.ShouldNotBeEmpty("the full adder ships its gates at top level");
        placements.SelectMany(p => p.Badges).Select(b => b.GroupName).Distinct()
            .ShouldBe(expected.Keys, ignoreOrder: true,
                customMessage: "a flat design keeps the plain group-name badge set");
        placements.ShouldAllBe(p => !p.Badges.Any(b => b.GroupName.Contains('/')),
            "no hierarchical gate id appears in a flat design");
        foreach (var placement in placements)
        {
            var gateId = placement.Badges.First().GroupName;
            placement.GroupBounds.ShouldBe(expected[gateId],
                $"the badges of '{gateId}' keep their pre-hierarchical position");
        }

        var markers = LogicGateRegisterMarkerRenderer.ComputeMarkers(canvas);
        var expectedMarkers = expected.Keys.Where(id =>
            canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
                .Single(g => g.GroupName == id).TruthTablePinAssignment!.IsRegister).ToList();
        markers.Select(m => m.GateId).ShouldBe(expectedMarkers, ignoreOrder: true,
            customMessage: "a flat design keeps the top-level register-marker set");
    }

    /// <summary>Builds the network through the real Logic panel so the canvas badges populate.</summary>
    private static async Task BuildPanelNetwork(DesignCanvasViewModel canvas)
    {
        var vm = new LogicPanelViewModel();
        vm.Configure(canvas);
        await vm.BuildNetworkCommand.ExecuteAsync(null);
        vm.HasNetwork.ShouldBeTrue(vm.StatusText);
        canvas.LogicGateStates.Badges.ShouldNotBeEmpty("the built network populates the canvas badges");
    }

    /// <summary>Resolves a hierarchical gate id (<c>CELL0/REG00</c>) to its nested group.</summary>
    private static ComponentGroup FindGroupByPath(DesignCanvasViewModel canvas, string gateId)
    {
        IEnumerable<Component> level = canvas.Components.Select(c => c.Component);
        ComponentGroup? found = null;
        foreach (var segment in gateId.Split('/'))
        {
            found = level.OfType<ComponentGroup>().Single(g => g.GroupName == segment);
            level = found.ChildComponents;
        }
        return found!;
    }
}

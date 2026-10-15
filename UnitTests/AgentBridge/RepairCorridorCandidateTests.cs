using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing.InterconnectRouting.SegmentShift;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// The corridor-widening repair candidates: fine step series, shifts of a wire that merely runs
/// past the blocked wire's pin (not a pin neighbour), and moves of the end component back from
/// its pin — widening direction first.
/// </summary>
public class RepairCorridorCandidateTests
{
    private const double PinGapMicrometers = 20;

    [Fact]
    public void CorridorSteps_AreFineAndBounded()
    {
        CorridorSteps.Widening(1).ShouldBe(new double[] { 4, 8, 12, 16, 20, 24, 28, 30 });
        CorridorSteps.Widening(-1).ShouldBe(new double[] { -4, -8, -12, -16, -20, -24, -28, -30 });
        CorridorSteps.WideningFirst(-1).Take(3).ShouldBe(new double[] { -4, -8, -12 });
        CorridorSteps.WideningFirst(-1).Skip(8).First().ShouldBe(4);
        CorridorSteps.Alternating().Take(4).ShouldBe(new double[] { 4, -4, 8, -8 });
        CorridorSteps.Alternating().Max().ShouldBe(CorridorSteps.MaxStepMicrometers);
    }

    [Fact]
    public async Task Generate_ShiftsAWireRunningPastThePin_AwayFromIt_AndMovesTheEndBackFromItsPin()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 3000, 3000);
        var a = AddWaveguide(canvas, "wgA", 100, 100);
        var b = AddWaveguide(canvas, "wgB", 1000, 400);
        var passing = canvas.ConnectPins(a.Component.PhysicalPins.Single(p => p.Name == "out"),
            b.Component.PhysicalPins.Single(p => p.Name == "in"))!.Connection;
        await canvas.RecalculateRoutesAsync();
        var handle = SegmentShiftGeometry.GetHandles(passing.GetPathSegments()).First();

        // A pin 20 µm off the passing wire's shiftable straight, on its normal side: the corridor
        // in front of it is bounded by a wire whose own pins are hundreds of µm away.
        double pinX = handle.Midpoint.X + handle.Normal.X * PinGapMicrometers;
        double pinY = handle.Midpoint.Y + handle.Normal.Y * PinGapMicrometers;
        var c = AddWaveguide(canvas, "wgC", pinX, pinY - 125);
        var d = AddWaveguide(canvas, "wgD", 2000, 2000);
        var blocked = canvas.ConnectPins(d.Component.PhysicalPins.Single(p => p.Name == "out"),
            c.Component.PhysicalPins.Single(p => p.Name == "in"))!.Connection;
        var session = new AgentDesignSession(canvas, new CommandManager(), () => null, new ExampleDesignsService());

        var candidates = new RepairCandidateGenerator(session, blocked).Generate();

        var shifts = candidates.Where(x => x.Kind == "shift_segment")
            .Select(x => Arguments(x))
            .Where(x => x.GetProperty("ConnectionId").GetString() == passing.Id.ToString())
            .ToList();
        shifts.ShouldNotBeEmpty("a wire running past the pin bounds its corridor");
        shifts[0].GetProperty("OffsetUm").GetDouble().ShouldBe(-CorridorSteps.FineStepMicrometers,
            "the pin lies on the normal side, so the first shift moves the straight the other way by the finest step");

        var firstMove = candidates.Where(x => x.Kind == "nudge_component")
            .Select(x => Arguments(x))
            .First(x => x.GetProperty("Id").GetString() == "wgC" && x.GetProperty("Dy").GetDouble() == 0);
        firstMove.GetProperty("Dx").GetDouble().ShouldBe(CorridorSteps.FineStepMicrometers,
            "the end component moves back from its west-facing pin (east) first");
    }

    [Fact]
    public async Task Generate_IsDeterministic()
    {
        var first = await DescribeCandidatesAsync();
        var second = await DescribeCandidatesAsync();
        second.ShouldBe(first);
    }

    private static async Task<string[]> DescribeCandidatesAsync()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 3000, 3000);
        var a = AddWaveguide(canvas, "wgA", 100, 100);
        var b = AddWaveguide(canvas, "wgB", 1000, 400);
        var wire = canvas.ConnectPins(a.Component.PhysicalPins.Single(p => p.Name == "out"),
            b.Component.PhysicalPins.Single(p => p.Name == "in"))!.Connection;
        await canvas.RecalculateRoutesAsync();
        var session = new AgentDesignSession(canvas, new CommandManager(), () => null, new ExampleDesignsService());
        return new RepairCandidateGenerator(session, wire).Generate().Select(x => x.Kind + ":" + x.Description).ToArray();
    }

    private static JsonElement Arguments(RepairCandidate candidate) =>
        JsonDocument.Parse(JsonSerializer.Serialize(candidate.Arguments)).RootElement.Clone();

    private static ComponentViewModel AddWaveguide(DesignCanvasViewModel canvas, string id, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.Identifier = id;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return canvas.AddComponent(component);
    }
}

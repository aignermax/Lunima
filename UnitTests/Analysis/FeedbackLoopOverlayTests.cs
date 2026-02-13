using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Analysis;

public class FeedbackLoopOverlayTests
{
    [Fact]
    public void Constructor_EmptyResult_AllSetsEmpty()
    {
        var result = new FeedbackLoopAnalysisResult();
        var overlay = new FeedbackLoopOverlay(result);

        overlay.LoopComponents.Count.ShouldBe(0);
        overlay.LoopConnections.Count.ShouldBe(0);
        overlay.UnstableComponents.Count.ShouldBe(0);
        overlay.UnstableConnections.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_StableLoop_PopulatesLoopSets()
    {
        var (components, connections) = CreateLoop(3, 0.5);
        var loop = new FeedbackLoop(components, connections, new Complex(0.125, 0));

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);

        var overlay = new FeedbackLoopOverlay(result);

        overlay.LoopComponents.Count.ShouldBe(3);
        overlay.LoopConnections.Count.ShouldBe(3);
        overlay.UnstableComponents.Count.ShouldBe(0);
        overlay.UnstableConnections.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_UnstableLoop_PopulatesUnstableSets()
    {
        var (components, connections) = CreateLoop(2, 1.0);
        var loop = new FeedbackLoop(components, connections, Complex.One);

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);

        var overlay = new FeedbackLoopOverlay(result);

        overlay.UnstableComponents.Count.ShouldBe(2);
        overlay.UnstableConnections.Count.ShouldBe(2);
    }

    [Fact]
    public void IsInLoop_ComponentInLoop_ReturnsTrue()
    {
        var (components, connections) = CreateLoop(2, 0.5);
        var loop = new FeedbackLoop(components, connections, new Complex(0.25, 0));

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);
        var overlay = new FeedbackLoopOverlay(result);

        overlay.IsInLoop(components[0]).ShouldBeTrue();
        overlay.IsInLoop(components[1]).ShouldBeTrue();
    }

    [Fact]
    public void IsInLoop_ComponentNotInLoop_ReturnsFalse()
    {
        var (components, connections) = CreateLoop(2, 0.5);
        var loop = new FeedbackLoop(components, connections, new Complex(0.25, 0));

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);
        var overlay = new FeedbackLoopOverlay(result);

        var outsider = CreateTestComponent("outsider");
        overlay.IsInLoop(outsider).ShouldBeFalse();
    }

    [Fact]
    public void IsUnstable_UnstableComponent_ReturnsTrue()
    {
        var (components, connections) = CreateLoop(2, 1.0);
        var loop = new FeedbackLoop(components, connections, Complex.One);

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);
        var overlay = new FeedbackLoopOverlay(result);

        overlay.IsUnstable(components[0]).ShouldBeTrue();
    }

    [Fact]
    public void IsUnstable_StableComponent_ReturnsFalse()
    {
        var (components, connections) = CreateLoop(2, 0.5);
        var loop = new FeedbackLoop(components, connections, new Complex(0.25, 0));

        var result = new FeedbackLoopAnalysisResult();
        result.AddLoop(loop);
        var overlay = new FeedbackLoopOverlay(result);

        overlay.IsUnstable(components[0]).ShouldBeFalse();
    }

    #region Helpers

    private static Component CreateTestComponent(string identifier)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("in0", 0, MatterType.Light, RectSide.Left),
            new("out0", 1, MatterType.Light, RectSide.Right)
        });

        var allPins = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
        var matrix = new SMatrix(allPins, new());

        var sMatrices = new Dictionary<int, SMatrix>
        {
            { 1550, matrix }
        };

        var component = new Component(
            sMatrices, new(), "test", "", parts, 0, identifier, DiscreteRotation.R0);

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in0",
            ParentComponent = component,
            LogicalPin = component.GetAllPins().First(p => p.Name == "in0")
        });
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out0",
            ParentComponent = component,
            LogicalPin = component.GetAllPins().First(p => p.Name == "out0")
        });

        return component;
    }

    private static (List<Component>, List<WaveguideConnection>) CreateLoop(
        int count, double transmission)
    {
        var components = new List<Component>();
        for (int i = 0; i < count; i++)
            components.Add(CreateTestComponent($"C{i}"));

        var connections = new List<WaveguideConnection>();
        for (int i = 0; i < count; i++)
        {
            var src = components[i];
            var tgt = components[(i + 1) % count];
            var conn = new WaveguideConnection
            {
                StartPin = src.PhysicalPins.First(p => p.Name == "out0"),
                EndPin = tgt.PhysicalPins.First(p => p.Name == "in0"),
            };
            typeof(WaveguideConnection)
                .GetProperty(nameof(WaveguideConnection.TransmissionCoefficient))!
                .SetValue(conn, new Complex(transmission, 0));
            connections.Add(conn);
        }

        return (components, connections);
    }

    #endregion
}

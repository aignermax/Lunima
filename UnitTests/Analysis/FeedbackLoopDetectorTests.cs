using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Analysis;

public class FeedbackLoopDetectorTests
{
    private readonly FeedbackLoopDetector _detector = new();

    [Fact]
    public void Analyze_EmptyNetwork_ReturnsNoLoops()
    {
        var result = _detector.Analyze(
            new List<Component>(),
            new List<WaveguideConnection>(),
            StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(0);
        result.HasUnstableLoops.ShouldBeFalse();
        result.Warnings.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_LinearChain_ReturnsNoLoops()
    {
        // A -> B -> C (no cycle)
        var (components, connections) = CreateLinearChain(3);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(0);
        result.HasUnstableLoops.ShouldBeFalse();
    }

    [Fact]
    public void Analyze_SimpleLoop_DetectsOneCycle()
    {
        // A -> B -> C -> A
        var (components, connections) = CreateSimpleLoop(3);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        result.Loops[0].Length.ShouldBe(3);
    }

    [Fact]
    public void Analyze_TwoComponentLoop_DetectsCycle()
    {
        // A -> B -> A
        var (components, connections) = CreateSimpleLoop(2);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        result.Loops[0].Length.ShouldBe(2);
    }

    [Fact]
    public void Analyze_SelfLoop_DetectsCycle()
    {
        // A -> A
        var component = CreateTestComponent("A");
        var connection = CreateTestConnection(component, component, 0.5);

        var result = _detector.Analyze(
            new List<Component> { component },
            new List<WaveguideConnection> { connection },
            StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        result.Loops[0].Length.ShouldBe(1);
    }

    [Fact]
    public void Analyze_LoopWithLowGain_IsStable()
    {
        // Loop with transmission 0.5 on each connection -> gain < 1
        var (components, connections) = CreateSimpleLoop(3, transmissionMagnitude: 0.5);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        result.Loops[0].IsUnstable.ShouldBeFalse();
        result.HasUnstableLoops.ShouldBeFalse();
        result.StableLoopCount.ShouldBe(1);
    }

    [Fact]
    public void Analyze_LoopWithHighGain_IsUnstable()
    {
        // Loop with transmission 1.0 on each connection -> gain = 1
        var (components, connections) = CreateSimpleLoop(2, transmissionMagnitude: 1.0);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        result.Loops[0].IsUnstable.ShouldBeTrue();
        result.HasUnstableLoops.ShouldBeTrue();
        result.UnstableLoopCount.ShouldBe(1);
    }

    [Fact]
    public void Analyze_UnstableLoop_GeneratesWarnings()
    {
        var (components, connections) = CreateSimpleLoop(2, transmissionMagnitude: 1.0);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.Warnings.Count.ShouldBeGreaterThan(0);
        result.Warnings.ShouldContain(w => w.Contains("Unstable"));
        result.Warnings.ShouldContain(w => w.Contains("oscillate"));
    }

    [Fact]
    public void Analyze_StableLoop_NoWarnings()
    {
        var (components, connections) = CreateSimpleLoop(3, transmissionMagnitude: 0.3);

        var result = _detector.Analyze(
            components, connections, StandardWaveLengths.RedNM);

        result.Warnings.Count.ShouldBe(0);
    }

    [Fact]
    public void Analyze_MultipleLoops_DetectsAll()
    {
        // Two separate loops: A->B->A and C->D->E->C
        var a = CreateTestComponent("A");
        var b = CreateTestComponent("B");
        var c = CreateTestComponent("C");
        var d = CreateTestComponent("D");
        var e = CreateTestComponent("E");

        var connections = new List<WaveguideConnection>
        {
            CreateTestConnection(a, b, 0.8),
            CreateTestConnection(b, a, 0.8),
            CreateTestConnection(c, d, 0.5),
            CreateTestConnection(d, e, 0.5),
            CreateTestConnection(e, c, 0.5),
        };

        var result = _detector.Analyze(
            new List<Component> { a, b, c, d, e },
            connections,
            StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(2);
    }

    [Fact]
    public void Analyze_LoopGainCalculation_UsesWaveguideTransmission()
    {
        // 2-component loop with known transmission values
        var a = CreateTestComponent("A");
        var b = CreateTestComponent("B");
        var connAB = CreateTestConnection(a, b, 0.9);
        var connBA = CreateTestConnection(b, a, 0.8);

        var result = _detector.Analyze(
            new List<Component> { a, b },
            new List<WaveguideConnection> { connAB, connBA },
            StandardWaveLengths.RedNM);

        result.LoopCount.ShouldBe(1);
        // Gain = 0.9 * 0.8 = 0.72 (waveguide only, no internal S-matrix lookup)
        result.Loops[0].LoopGainMagnitude.ShouldBe(0.72, tolerance: 0.01);
    }

    [Fact]
    public void DetectCycles_NoCycles_ReturnsEmpty()
    {
        var a = CreateTestComponent("A");
        var b = CreateTestComponent("B");
        var adjacency = new Dictionary<Component, List<(Component, WaveguideConnection)>>
        {
            { a, new() { (b, CreateTestConnection(a, b, 1.0)) } },
            { b, new() }
        };

        var cycles = FeedbackLoopDetector.DetectCycles(adjacency);
        cycles.Count.ShouldBe(0);
    }

    [Fact]
    public void AreCyclicPermutations_SameSequence_ReturnsTrue()
    {
        var list1 = new List<int> { 1, 2, 3 };
        var list2 = new List<int> { 1, 2, 3 };

        FeedbackLoopDetector.AreCyclicPermutations(list1, list2).ShouldBeTrue();
    }

    [Fact]
    public void AreCyclicPermutations_RotatedSequence_ReturnsTrue()
    {
        var list1 = new List<int> { 1, 2, 3 };
        var list2 = new List<int> { 2, 3, 1 };

        FeedbackLoopDetector.AreCyclicPermutations(list1, list2).ShouldBeTrue();
    }

    [Fact]
    public void AreCyclicPermutations_DifferentSequence_ReturnsFalse()
    {
        var list1 = new List<int> { 1, 2, 3 };
        var list2 = new List<int> { 1, 3, 2 };

        FeedbackLoopDetector.AreCyclicPermutations(list1, list2).ShouldBeFalse();
    }

    [Fact]
    public void AreCyclicPermutations_DifferentLengths_ReturnsFalse()
    {
        var list1 = new List<int> { 1, 2 };
        var list2 = new List<int> { 1, 2, 3 };

        FeedbackLoopDetector.AreCyclicPermutations(list1, list2).ShouldBeFalse();
    }

    #region Helper Methods

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

        // Set identity-like transfer (input left -> output right)
        var leftIn = parts[0, 0].GetPinAt(RectSide.Left)!.IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right)!.IDOutFlow;
        matrix.SetValues(new() { { (leftIn, rightOut), Complex.One } });

        var sMatrices = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, matrix }
        };

        var component = new Component(
            sMatrices, new(), "test", "", parts, 0, identifier, DiscreteRotation.R0);

        // Create physical pins linked to logical pins
        var logicalPins = component.GetAllPins();
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in0",
            ParentComponent = component,
            LogicalPin = logicalPins.First(p => p.Name == "in0")
        });
        component.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out0",
            ParentComponent = component,
            LogicalPin = logicalPins.First(p => p.Name == "out0")
        });

        return component;
    }

    private static WaveguideConnection CreateTestConnection(
        Component source, Component target, double transmissionMagnitude)
    {
        var startPin = source.PhysicalPins.First(p => p.Name == "out0");
        var endPin = target.PhysicalPins.First(p => p.Name == "in0");

        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
        };

        // Use reflection to set private setter (same pattern as BasicSMatrixValidatorTests)
        typeof(WaveguideConnection)
            .GetProperty(nameof(WaveguideConnection.TransmissionCoefficient))!
            .SetValue(connection, new Complex(transmissionMagnitude, 0));

        return connection;
    }

    private static (List<Component> Components, List<WaveguideConnection> Connections)
        CreateSimpleLoop(int count, double transmissionMagnitude = 0.8)
    {
        var components = new List<Component>();
        for (int i = 0; i < count; i++)
        {
            components.Add(CreateTestComponent($"C{i}"));
        }

        var connections = new List<WaveguideConnection>();
        for (int i = 0; i < count; i++)
        {
            var source = components[i];
            var target = components[(i + 1) % count];
            connections.Add(CreateTestConnection(source, target, transmissionMagnitude));
        }

        return (components, connections);
    }

    private static (List<Component> Components, List<WaveguideConnection> Connections)
        CreateLinearChain(int count)
    {
        var components = new List<Component>();
        for (int i = 0; i < count; i++)
        {
            components.Add(CreateTestComponent($"C{i}"));
        }

        var connections = new List<WaveguideConnection>();
        for (int i = 0; i < count - 1; i++)
        {
            connections.Add(CreateTestConnection(
                components[i], components[i + 1], 0.8));
        }

        return (components, connections);
    }

    #endregion
}

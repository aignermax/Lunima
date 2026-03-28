using System.Numerics;
using CAP.Avalonia.Visualization;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Visualization;

/// <summary>
/// Integration tests verifying that frozen waveguides inside ComponentGroups
/// show distinct colors (different power levels) rather than uniform color.
///
/// This is the acceptance test for GitHub issue #323:
/// "Frozen waveguides in ComponentGroups show uniform color without individual loss values"
/// </summary>
public class InternalFieldVisualizationTests
{
    /// <summary>
    /// Core acceptance test for issue #323:
    /// A group with three frozen paths of different transmission (high/medium/low)
    /// must produce three distinct power fractions — confirming different visualization colors.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_GroupWithThreeDifferentPaths_ShowsDistinctColors()
    {
        // Arrange
        var visualizer = new PowerFlowVisualizer();

        var (group, paths, entryExtPin) = CreateGroupWithThreeDifferentLossPaths();
        var components = new List<Component> { group };
        var connections = new List<WaveguideConnection>();

        // Only external group pin amplitude in fieldResults (realistic post-grouping scenario)
        var fieldResults = new Dictionary<Guid, Complex>
        {
            [entryExtPin.IDInFlow] = new Complex(1.0, 0)
        };

        // Act
        visualizer.UpdateFromSimulation(connections, components, fieldResults);

        // Assert: all three paths should have non-zero power
        visualizer.CurrentResult.ShouldNotBeNull();

        var flow0 = visualizer.CurrentResult!.ConnectionFlows[paths[0].PathId];
        var flow1 = visualizer.CurrentResult.ConnectionFlows[paths[1].PathId];
        var flow2 = visualizer.CurrentResult.ConnectionFlows[paths[2].PathId];

        flow0.AveragePower.ShouldBeGreaterThan(0,
            "High-transmission path should show non-zero power.");
        flow1.AveragePower.ShouldBeGreaterThan(0,
            "Medium-transmission path should show non-zero power.");
        flow2.AveragePower.ShouldBeGreaterThan(0,
            "Low-transmission path should show non-zero power.");

        // The three paths should have different power levels (not uniform)
        var powers = new[] { flow0.AveragePower, flow1.AveragePower, flow2.AveragePower };
        var distinctPowers = powers.Distinct().Count();
        distinctPowers.ShouldBeGreaterThan(1,
            "Frozen paths with different transmission should have distinct power values, " +
            "not uniform color. This is the fix for issue #323.");
    }

    /// <summary>
    /// Verifies that the highest-transmission path has the highest power fraction,
    /// and the lowest-transmission path has the lowest — confirming physical accuracy.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_PathsWithDecreasingTransmission_ShowDecreasingPower()
    {
        var visualizer = new PowerFlowVisualizer();

        var (group, paths, entryExtPin) = CreateGroupWithThreeDifferentLossPaths();
        var components = new List<Component> { group };

        var fieldResults = new Dictionary<Guid, Complex>
        {
            [entryExtPin.IDInFlow] = new Complex(1.0, 0)
        };

        visualizer.UpdateFromSimulation(new List<WaveguideConnection>(), components, fieldResults);

        visualizer.CurrentResult.ShouldNotBeNull();

        var power0 = visualizer.CurrentResult!.ConnectionFlows[paths[0].PathId].AveragePower;
        var power1 = visualizer.CurrentResult.ConnectionFlows[paths[1].PathId].AveragePower;
        var power2 = visualizer.CurrentResult.ConnectionFlows[paths[2].PathId].AveragePower;

        // Paths are in a linear chain: path0 feeds path1 feeds path2
        // So power should decrease along the chain
        power0.ShouldBeGreaterThan(power2,
            "First path (upstream) should have higher power than last path (downstream).");
    }

    /// <summary>
    /// Verifies that none of the frozen paths are incorrectly faded out
    /// when light is present at the group's external pin.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_ActiveLightInGroup_FrozenPathsNotFadedOut()
    {
        var visualizer = new PowerFlowVisualizer();

        var (group, paths, entryExtPin) = CreateGroupWithThreeDifferentLossPaths();
        var components = new List<Component> { group };

        var fieldResults = new Dictionary<Guid, Complex>
        {
            [entryExtPin.IDInFlow] = new Complex(1.0, 0)
        };

        visualizer.UpdateFromSimulation(new List<WaveguideConnection>(), components, fieldResults);

        visualizer.CurrentResult.ShouldNotBeNull();

        // High and medium transmission paths should not be faded (sufficient power)
        visualizer.CurrentResult!.IsFadedOut(paths[0].PathId).ShouldBeFalse(
            "High-transmission path should not be faded with active light input.");
        visualizer.CurrentResult.IsFadedOut(paths[1].PathId).ShouldBeFalse(
            "Medium-transmission path should not be faded with active light input.");
    }

    /// <summary>
    /// Verifies backward compatibility: existing tests relying on the fallback behavior
    /// (groups without S-Matrices) still produce non-zero power via the fallback path.
    /// </summary>
    [Fact]
    public void UpdateFromSimulation_GroupWithoutSMatrix_FallbackStillProducesNonZeroPower()
    {
        var visualizer = new PowerFlowVisualizer();

        // Create group with components that have no S-Matrices (simulates legacy behavior)
        var (group, frozenPath, externalLogPin) = CreateGroupWithoutSMatrix();
        var components = new List<Component> { group };

        var fieldResults = new Dictionary<Guid, Complex>
        {
            [externalLogPin.IDOutFlow] = new Complex(1.0, 0)
        };

        visualizer.UpdateFromSimulation(new List<WaveguideConnection>(), components, fieldResults);

        visualizer.CurrentResult.ShouldNotBeNull();
        var flow = visualizer.CurrentResult!.ConnectionFlows[frozenPath.PathId];
        flow.AveragePower.ShouldBeGreaterThan(0,
            "Fallback should still provide non-zero power for groups without S-Matrices.");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a ComponentGroup with three frozen paths in a linear chain.
    /// Transmissions: path0 = 0.95 (low loss), path1 = 0.70 (medium loss), path2 = 0.30 (high loss).
    /// The first component's input pin is the external entry point.
    /// </summary>
    private static (ComponentGroup group, List<FrozenWaveguidePath> paths, Pin entryPin)
        CreateGroupWithThreeDifferentLossPaths()
    {
        var group = new ComponentGroup("ThreePathGroup");
        int wl = StandardWaveLengths.RedNM;

        var transmissions = new[] { 0.95, 0.70, 0.30 };
        var comps = new List<(Component c, Pin logIn, Pin logOut, PhysicalPin physIn, PhysicalPin physOut)>();

        for (int i = 0; i <= transmissions.Length; i++)
        {
            var t = CreateWaveguideComp($"c{i}", i * 150.0, wl);
            comps.Add(t);
            group.AddChild(t.c);
        }

        var paths = new List<FrozenWaveguidePath>();
        for (int i = 0; i < transmissions.Length; i++)
        {
            var path = MakeFrozenPath(
                comps[i].physOut,
                comps[i + 1].physIn,
                transmissions[i]);
            group.AddInternalPath(path);
            paths.Add(path);
        }

        // Expose entry pin of first component
        group.AddExternalPin(new GroupPin
        {
            Name = "entry",
            InternalPin = comps[0].physIn,
            RelativeX = 0,
            RelativeY = 5,
            AngleDegrees = 180
        });

        return (group, paths, comps[0].logIn);
    }

    /// <summary>
    /// Creates a group with components that have no S-Matrices (for fallback testing).
    /// </summary>
    private static (ComponentGroup group, FrozenWaveguidePath path, Pin extPin)
        CreateGroupWithoutSMatrix()
    {
        var group = new ComponentGroup("NoSMatrixGroup");

        var logIn = new Pin("in", 0, MatterType.Light, RectSide.Left);
        var logOut = new Pin("out", 1, MatterType.Light, RectSide.Right);

        var physIn = new PhysicalPin { Name = "in", LogicalPin = logIn };
        var physOut = new PhysicalPin { Name = "out", LogicalPin = logOut };

        // Components with no S-Matrix
        var compA = new Component(
            new Dictionary<int, SMatrix>(), new List<Slider>(), "test", "",
            new Part[1, 1] { { new Part() } }, 0, "compA",
            new DiscreteRotation(), new List<PhysicalPin> { physIn, physOut })
        { PhysicalX = 0, PhysicalY = 0 };
        physIn.ParentComponent = compA;
        physOut.ParentComponent = compA;

        var logIn2 = new Pin("in2", 2, MatterType.Light, RectSide.Left);
        var physIn2 = new PhysicalPin { Name = "in2", LogicalPin = logIn2 };

        var compB = new Component(
            new Dictionary<int, SMatrix>(), new List<Slider>(), "test", "",
            new Part[1, 1] { { new Part() } }, 0, "compB",
            new DiscreteRotation(), new List<PhysicalPin> { physIn2 })
        { PhysicalX = 100, PhysicalY = 0 };
        physIn2.ParentComponent = compB;

        group.AddChild(compA);
        group.AddChild(compB);

        var routedPath = new RoutedPath();
        routedPath.Segments.Add(new StraightSegment(10, 5, 100, 5, 0));
        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = routedPath,
            StartPin = physOut,
            EndPin = physIn2
        };
        group.AddInternalPath(frozenPath);

        group.AddExternalPin(new GroupPin
        {
            Name = "ext",
            InternalPin = physOut,
            RelativeX = 10,
            RelativeY = 5,
            AngleDegrees = 0
        });

        return (group, frozenPath, logOut);
    }

    private static (Component c, Pin logIn, Pin logOut, PhysicalPin physIn, PhysicalPin physOut)
        CreateWaveguideComp(string id, double x, int wl)
    {
        var logIn = new Pin($"in_{id}", 0, MatterType.Light, RectSide.Left);
        var logOut = new Pin($"out_{id}", 1, MatterType.Light, RectSide.Right);

        var allIds = new List<Guid> { logIn.IDInFlow, logIn.IDOutFlow, logOut.IDInFlow, logOut.IDOutFlow };
        var sm = new SMatrix(allIds, new());
        sm.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (logIn.IDInFlow, logOut.IDOutFlow), Complex.One },
            { (logOut.IDInFlow, logIn.IDOutFlow), Complex.One }
        });

        var physIn = new PhysicalPin { Name = $"in_{id}", LogicalPin = logIn, OffsetXMicrometers = 0, OffsetYMicrometers = 5 };
        var physOut = new PhysicalPin { Name = $"out_{id}", LogicalPin = logOut, OffsetXMicrometers = 10, OffsetYMicrometers = 5 };

        var comp = new Component(
            new Dictionary<int, SMatrix> { [wl] = sm }, new List<Slider>(), "wg", "",
            new Part[1, 1] { { new Part() } }, 0, id,
            new DiscreteRotation(), new List<PhysicalPin> { physIn, physOut })
        { PhysicalX = x, PhysicalY = 0, WidthMicrometers = 10, HeightMicrometers = 10 };

        physIn.ParentComponent = comp;
        physOut.ParentComponent = comp;

        return (comp, logIn, logOut, physIn, physOut);
    }

    private static FrozenWaveguidePath MakeFrozenPath(
        PhysicalPin startPin,
        PhysicalPin endPin,
        double transmissionAmplitude)
    {
        double lossDb = -20.0 * Math.Log10(transmissionAmplitude);
        double lengthCm = lossDb / 0.5;
        double lengthUm = lengthCm * 10_000.0;

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 5, lengthUm, 5, 0));

        return new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = path,
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = 0.5
        };
    }
}

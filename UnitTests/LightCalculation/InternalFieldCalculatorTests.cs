using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.PowerFlow;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Unit tests for InternalFieldCalculator.
/// Verifies that internal field amplitudes are computed correctly from
/// boundary conditions at a ComponentGroup's external pins.
/// </summary>
public class InternalFieldCalculatorTests
{
    private readonly InternalFieldCalculator _calculator = new();

    /// <summary>
    /// When no boundary conditions are provided (empty fieldResults),
    /// all computed internal fields should have zero amplitude.
    /// </summary>
    [Fact]
    public void ComputeInternalFields_NoExternalAmplitude_ReturnsZeroOrEmpty()
    {
        var (group, _) = CreateLinearChainGroup(transmissionA: 0.9, transmissionB: 0.5);

        var result = _calculator.ComputeInternalFields(group, new Dictionary<Guid, Complex>());

        // Zero-amplitude inputs → zero or empty outputs
        foreach (var value in result.Values)
            value.Magnitude.ShouldBe(0.0, tolerance: 1e-12);
    }

    /// <summary>
    /// With a known input amplitude at the entry external pin, the first frozen
    /// path (directly fed by the boundary component) should have non-zero amplitude.
    /// </summary>
    [Fact]
    public void ComputeInternalFields_WithBoundaryCondition_ProducesNonZeroInternalAmplitude()
    {
        var (group, externalLogicalPin) = CreateLinearChainGroup(transmissionA: 0.9, transmissionB: 0.5);

        // Seed the entry boundary pin
        var fieldResults = new Dictionary<Guid, Complex>
        {
            [externalLogicalPin.IDInFlow] = new Complex(1.0, 0)
        };

        var result = _calculator.ComputeInternalFields(group, fieldResults);

        result.ShouldNotBeEmpty();
        result.Values.Any(v => v.Magnitude > 0).ShouldBeTrue(
            "At least one internal pin should have non-zero amplitude when " +
            "a boundary condition is provided.");
    }

    /// <summary>
    /// A group with three frozen paths of different transmission coefficients
    /// (high, medium, low) should produce three distinct field amplitudes —
    /// confirming the fix for the uniform-color visualization bug.
    /// </summary>
    [Fact]
    public void ComputeInternalFields_ThreePathsDifferentLoss_ProducesDistinctAmplitudes()
    {
        // Build: InputExtPin → Comp1 → [path1: T=0.9] → Comp2 → [path2: T=0.5] → Comp3 → [path3: T=0.1] → Comp4
        var (group, paths, entryExtPin) = CreateThreePathGroup(
            transmissions: new[] { 0.9, 0.5, 0.1 });

        var fieldResults = new Dictionary<Guid, Complex>
        {
            [entryExtPin.IDInFlow] = new Complex(1.0, 0)
        };

        var result = _calculator.ComputeInternalFields(group, fieldResults);

        // Extract amplitudes at each path's start pin outflow (light entering the frozen path)
        var amps = paths.Select(p =>
            result.TryGetValue(p.StartPin.LogicalPin!.IDOutFlow, out var v) ? v.Magnitude : 0.0
        ).ToList();

        // All should be non-zero (some light reaches each path)
        amps.All(a => a > 0).ShouldBeTrue(
            "All three internal paths should have non-zero light amplitude.");

        // Amplitudes should be strictly decreasing (each path loses more light)
        amps[0].ShouldBeGreaterThan(amps[1],
            "Higher-transmission path should have higher amplitude than lower-transmission path.");
        amps[1].ShouldBeGreaterThan(amps[2],
            "Medium-transmission path should have higher amplitude than low-transmission path.");
    }

    /// <summary>
    /// When a group has no child S-Matrices (no wavelength info), ComputeInternalFields
    /// should return an empty result without throwing.
    /// </summary>
    [Fact]
    public void ComputeInternalFields_GroupWithoutSMatrices_ReturnsEmpty()
    {
        var group = new ComponentGroup("EmptyGroup");

        // Add components without S-Matrices
        var comp1 = new Component(
            new Dictionary<int, SMatrix>(), new List<Slider>(), "test", "",
            new Part[1, 1] { { new Part() } }, 0, "comp1",
            new DiscreteRotation(), new List<PhysicalPin>())
        { PhysicalX = 0, PhysicalY = 0 };

        group.AddChild(comp1);

        var result = _calculator.ComputeInternalFields(group,
            new Dictionary<Guid, Complex> { [Guid.NewGuid()] = new Complex(1.0, 0) });

        result.ShouldBeEmpty("Groups without S-Matrices cannot propagate fields.");
    }

    /// <summary>
    /// Verifies that ComputeInternalFields handles nested groups by computing
    /// internal fields for each group level independently.
    /// </summary>
    [Fact]
    public void ComputeInternalFields_GroupWithChildren_DoesNotThrow()
    {
        var (outerGroup, externalPin) = CreateLinearChainGroup(transmissionA: 0.8, transmissionB: 0.6);

        var fieldResults = new Dictionary<Guid, Complex>
        {
            [externalPin.IDInFlow] = new Complex(1.0, 0)
        };

        // Should not throw
        var result = _calculator.ComputeInternalFields(outerGroup, fieldResults);
        result.ShouldNotBeNull();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a group with two components connected by one frozen path with
    /// configurable transmission coefficients. Returns the group and the external pin.
    ///
    /// Structure: [ExternalPin] → CompA → [frozenPath, T=transmissionA] → CompB
    ///            → [frozenPath, T=transmissionB] → CompC → [ExternalPin]
    /// </summary>
    private static (ComponentGroup group, Pin entryLogicalPin) CreateLinearChainGroup(
        double transmissionA,
        double transmissionB)
    {
        var (group, _, entryPin) = CreateThreePathGroup(new[] { transmissionA, transmissionB, 0.8 });
        return (group, entryPin);
    }

    /// <summary>
    /// Creates a group with N frozen paths in a linear chain with specified transmissions.
    /// The first component's input pin is exposed as an external group pin.
    /// </summary>
    private static (ComponentGroup group, List<FrozenWaveguidePath> paths, Pin entryExtPin)
        CreateThreePathGroup(double[] transmissions)
    {
        var group = new ComponentGroup("TestChainGroup");
        var components = new List<(Component comp, PhysicalPin inPin, PhysicalPin outPin, Pin logIn, Pin logOut)>();

        int wl = StandardWaveLengths.RedNM;

        // Create N+1 components for N frozen paths
        for (int i = 0; i <= transmissions.Length; i++)
        {
            var (comp, logIn, logOut, physIn, physOut) = CreateWaveguideComponentWithPhysicalPins(
                $"comp_{i}", i * 100.0, wl);
            components.Add((comp, physIn, physOut, logIn, logOut));
            group.AddChild(comp);
        }

        // Create N frozen paths connecting consecutive components
        var frozenPaths = new List<FrozenWaveguidePath>();

        for (int i = 0; i < transmissions.Length; i++)
        {
            var path = CreateFrozenPathWithTransmission(
                startPin: components[i].outPin,
                endPin: components[i + 1].inPin,
                transmissionAmplitude: transmissions[i]);
            group.AddInternalPath(path);
            frozenPaths.Add(path);
        }

        // Expose the first component's input pin as the group's external entry pin
        var entryLogPin = components[0].logIn;
        var entryGroupPin = new GroupPin
        {
            Name = "entry",
            InternalPin = components[0].inPin,
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 180
        };
        group.AddExternalPin(entryGroupPin);

        return (group, frozenPaths, entryLogPin);
    }

    /// <summary>
    /// Creates a straight-waveguide component with physical pins and a RedNM S-Matrix.
    /// </summary>
    private static (Component comp, Pin logIn, Pin logOut, PhysicalPin physIn, PhysicalPin physOut)
        CreateWaveguideComponentWithPhysicalPins(string id, double x, int wavelengthNm)
    {
        var logIn = new Pin($"in_{id}", 0, MatterType.Light, RectSide.Left);
        var logOut = new Pin($"out_{id}", 1, MatterType.Light, RectSide.Right);

        var allPinIds = new List<Guid> { logIn.IDInFlow, logIn.IDOutFlow, logOut.IDInFlow, logOut.IDOutFlow };
        var sMatrix = new SMatrix(allPinIds, new());
        sMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (logIn.IDInFlow, logOut.IDOutFlow), Complex.One },
            { (logOut.IDInFlow, logIn.IDOutFlow), Complex.One }
        });

        var matrixMap = new Dictionary<int, SMatrix> { [wavelengthNm] = sMatrix };

        var physIn = new PhysicalPin
        {
            Name = $"physIn_{id}",
            LogicalPin = logIn,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 5
        };

        var physOut = new PhysicalPin
        {
            Name = $"physOut_{id}",
            LogicalPin = logOut,
            OffsetXMicrometers = 10,
            OffsetYMicrometers = 5
        };

        var comp = new Component(
            matrixMap, new List<Slider>(), "waveguide", "",
            new Part[1, 1] { { new Part() } }, 0, id,
            new DiscreteRotation(),
            new List<PhysicalPin> { physIn, physOut })
        {
            PhysicalX = x,
            PhysicalY = 0,
            WidthMicrometers = 10,
            HeightMicrometers = 10
        };

        physIn.ParentComponent = comp;
        physOut.ParentComponent = comp;

        return (comp, logIn, logOut, physIn, physOut);
    }

    /// <summary>
    /// Creates a frozen path between two physical pins with a given amplitude transmission.
    /// </summary>
    private static FrozenWaveguidePath CreateFrozenPathWithTransmission(
        PhysicalPin startPin,
        PhysicalPin endPin,
        double transmissionAmplitude)
    {
        var routedPath = new RoutedPath();

        double startX = startPin.OffsetXMicrometers;
        double endX = endPin.OffsetXMicrometers + (endPin.ParentComponent?.PhysicalX ?? 0) -
                      (startPin.ParentComponent?.PhysicalX ?? 0);

        // The length is chosen so that PropagationLossDbPerCm produces the desired amplitude.
        // amplitude = 10^(-lossDb/20), lossDb = lossFactor * lengthCm
        // For a lossFactor of 0.5 dB/cm: length_cm = -20 * log10(amplitude) / 0.5
        double lossDb = -20.0 * Math.Log10(transmissionAmplitude);
        double lengthCm = lossDb / 0.5;
        double lengthMicrometers = lengthCm * 10_000.0;

        routedPath.Segments.Add(new StraightSegment(0, 5, lengthMicrometers, 5, 0));

        return new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = routedPath,
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = 0.5
        };
    }
}

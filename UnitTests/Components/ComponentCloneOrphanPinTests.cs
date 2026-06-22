using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Regression tests for <see cref="Component.Clone"/> when the S-matrix references pin IDs
/// that are no longer present in the component's Parts — the situation a raw-code Nazca
/// override creates when it replaces the component's ports (#561). Cloning (copy/paste)
/// must not throw a <see cref="KeyNotFoundException"/>; orphaned connections are dropped.
/// </summary>
public class ComponentCloneOrphanPinTests
{
    private static Component CreateComponentWithOrphanSMatrixPin(out int validConnectionCount)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });

        var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;

        // Pins that exist in the S-matrix but NOT in Parts — simulates a raw-code override
        // having replaced the ports while a stale S-matrix entry still references the old pin.
        var orphanIn = Guid.NewGuid();
        var orphanOut = Guid.NewGuid();

        var partPinIds = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow });
        var allPinIds = partPinIds.Concat(new[] { orphanIn, orphanOut }).ToList();

        var matrix = new SMatrix(allPinIds, new());
        matrix.SetValues(new()
        {
            { (leftIn, rightOut), Complex.One },   // valid: both pins in Parts
            { (orphanIn, orphanOut), 0.5 },        // orphan: pins absent from Parts
        });

        var connections = new Dictionary<int, SMatrix> { { StandardWaveLengths.RedNM, matrix } };
        validConnectionCount = 1;
        return new Component(connections, new(), "placeCell_StraightWG", "", parts, 0, "Straight", DiscreteRotation.R0);
    }

    [Fact]
    public void Clone_SMatrixReferencesPinAbsentFromParts_DoesNotThrow()
    {
        var component = CreateComponentWithOrphanSMatrixPin(out _);

        Should.NotThrow(() => component.Clone());
    }

    [Fact]
    public void Clone_DropsOrphanConnection_ButKeepsValidOne()
    {
        var component = CreateComponentWithOrphanSMatrixPin(out var validConnectionCount);

        var clone = (Component)component.Clone();

        var clonedMatrix = clone.WaveLengthToSMatrixMap[StandardWaveLengths.RedNM];
        clonedMatrix.GetNonNullValues().Count.ShouldBe(validConnectionCount,
            "The in-Parts connection must survive cloning; the orphan connection is dropped.");
    }

    [Fact]
    public void Clone_PreservesOverriddenSMatrixValue_NotJustStructure()
    {
        // Reproduces the copy/paste concern for an FDTD-recomputed S-matrix on NORMAL
        // ports: the recompute writes non-default values onto the component's existing
        // pins (all present in Parts). Cloning must carry the *values* over, not reset
        // to a PDK default. This confirms Clone is NOT where an FDTD override is lost.
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });
        var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;
        var allPinIds = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();

        var overridden = new Complex(0.1234, -0.5678);   // a distinctly non-default value
        var matrix = new SMatrix(allPinIds, new());
        matrix.SetValues(new() { { (leftIn, rightOut), overridden } });
        var component = new Component(
            new Dictionary<int, SMatrix> { { StandardWaveLengths.RedNM, matrix } },
            new(), "placeCell_StraightWG", "", parts, 0, "Straight", DiscreteRotation.R0);

        var clone = (Component)component.Clone();

        var clonedPins = Component.GetAllPins(clone.Parts).ToList();
        var clonedLeftIn = clonedPins.Single(p => p.Name == "west0").IDInFlow;
        var clonedRightOut = clonedPins.Single(p => p.Name == "east0").IDOutFlow;
        var clonedValues = clone.WaveLengthToSMatrixMap[StandardWaveLengths.RedNM].GetNonNullValues();
        clonedValues[(clonedLeftIn, clonedRightOut)].ShouldBe(overridden,
            "Clone must preserve the overridden S-matrix value on the (re-keyed) pins.");
    }
}

using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Tests for the shared <see cref="PirSMatrixExtractor"/> that underlies the
/// Verilog-A and PICWave export pipelines. Covers all three exit paths: a
/// successful pin-GUID lookup, the null return when no S-matrix is registered
/// at the target wavelength, and the two loud-failure guards (missing
/// LogicalPin, and registered S-matrix whose GUIDs drifted from the
/// component's PhysicalPins).
/// </summary>
public class PirSMatrixExtractorTests
{
    [Fact]
    public void TryExtract_NoSMatrixAtWavelength_ReturnsNull()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = PirSMatrixExtractor.TryExtract(comp, wavelengthNm: 9999);

        result.ShouldBeNull();
    }

    [Fact]
    public void TryExtract_SMatrixRegistered_ReturnsMapKeyedByPhysicalPinOrder()
    {
        // CreateStraightWaveGuideWithPhysicalPins registers a straight-through
        // S-matrix at the standard wavelengths with S21 = S12 = 1.
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();

        var result = PirSMatrixExtractor.TryExtract(comp, CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM);

        result.ShouldNotBeNull();
        result!.ShouldContainKey((1, 0));  // out-port index 1 ← in-port index 0
        result[(1, 0)].ShouldBe(new Complex(1, 0));
        result.ShouldContainKey((0, 1));
        result[(0, 1)].ShouldBe(new Complex(1, 0));
    }

    [Fact]
    public void TryExtract_PinWithoutLogicalPin_Throws()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        // Sever the LogicalPin reference on the first PhysicalPin — simulates
        // an incomplete component model where pins were declared but never wired.
        comp.PhysicalPins[0].LogicalPin = null;

        var ex = Should.Throw<InvalidOperationException>(
            () => PirSMatrixExtractor.TryExtract(comp, CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM));

        ex.Message.ShouldContain("LogicalPin");
        ex.Message.ShouldContain(comp.PhysicalPins[0].Name);
    }

    [Fact]
    public void TryExtract_SMatrixKeysDoNotMatchPhysicalPins_ThrowsOutOfSync()
    {
        // Build a component with real PhysicalPins/LogicalPins, then install an
        // S-matrix whose non-null entries are keyed against unrelated GUIDs —
        // this is what "SMatrix drift" looks like after a refactor that rebuilt
        // PhysicalPins without re-seeding the SMatrix.
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        int wl = CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM;

        var unrelatedInGuid = Guid.NewGuid();
        var unrelatedOutGuid = Guid.NewGuid();
        var driftedMatrix = new SMatrix(
            new List<Guid> { unrelatedInGuid, unrelatedOutGuid },
            new List<(Guid sliderID, double value)>());
        driftedMatrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (unrelatedInGuid, unrelatedOutGuid), new Complex(0.9, 0) }
        });
        comp.WaveLengthToSMatrixMap[wl] = driftedMatrix;

        var ex = Should.Throw<InvalidOperationException>(
            () => PirSMatrixExtractor.TryExtract(comp, wl));

        ex.Message.ShouldContain("out of sync");
        ex.Message.ShouldContain(comp.Name);
    }
}

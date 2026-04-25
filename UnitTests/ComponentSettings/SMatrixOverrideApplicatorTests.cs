using CAP.Avalonia.Services;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests;
using Xunit;

namespace UnitTests.ComponentSettings;

/// <summary>
/// Tests for <see cref="SMatrixOverrideApplicator"/>.
/// Verifies that stored S-matrix data is applied correctly to live components.
/// </summary>
public class SMatrixOverrideApplicatorTests
{
    private static ComponentSMatrixData MakeData(string wavelengthKey, int portCount)
    {
        int n = portCount;
        var real = new List<double>();
        var imag = new List<double>();

        for (int i = 0; i < n * n; i++)
        {
            real.Add(i / n == i % n ? 0.9 : 0.05);
            imag.Add(0.0);
        }

        var data = new ComponentSMatrixData { SourceNote = "Test" };
        data.Wavelengths[wavelengthKey] = new SMatrixWavelengthEntry
        {
            Rows = n,
            Cols = n,
            Real = real,
            Imag = imag
        };

        return data;
    }

    [Fact]
    public void Apply_ValidData_ReturnsTrue()
    {
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        var data = MakeData("1550", 2);

        var applied = SMatrixOverrideApplicator.Apply(component, data);

        applied.ShouldBeTrue();
    }

    [Fact]
    public void Apply_ValidData_UpdatesWavelengthMapForThatWavelength()
    {
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        var originalMatrices = component.WaveLengthToSMatrixMap.Count;
        var data = MakeData("1550", 2);

        SMatrixOverrideApplicator.Apply(component, data);

        component.WaveLengthToSMatrixMap.ShouldContainKey(1550);
    }

    [Fact]
    public void Apply_NonSquareEntry_IsSkipped()
    {
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        var data = new ComponentSMatrixData();
        data.Wavelengths["1550"] = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 3,   // non-square — must be rejected
            Real = new List<double>(new double[6]),
            Imag = new List<double>(new double[6])
        };

        var applied = SMatrixOverrideApplicator.Apply(component, data);

        applied.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ComponentWithNoPhysicalPins_ReturnsFalse()
    {
        // CreateBasicComponent has no physical pins
        var component = TestComponentFactory.CreateBasicComponent();
        var data = MakeData("1550", 2);

        var applied = SMatrixOverrideApplicator.Apply(component, data);

        applied.ShouldBeFalse();
    }

    [Fact]
    public void Apply_MalformedRealArray_IsSkipped()
    {
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        var data = new ComponentSMatrixData();
        data.Wavelengths["1550"] = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 0.9 },   // too short
            Imag = new List<double>(new double[4])
        };

        var applied = SMatrixOverrideApplicator.Apply(component, data);

        applied.ShouldBeFalse();
    }

    [Fact]
    public void ApplyAll_OnlyOverridesMatchingComponent()
    {
        var comp1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp1.Identifier = "comp_1";
        var comp2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        comp2.Identifier = "comp_2";

        // Record comp2's original wavelength keys
        var comp2OriginalKeys = comp2.WaveLengthToSMatrixMap.Keys.ToHashSet();

        var store = new Dictionary<string, ComponentSMatrixData>
        {
            ["comp_1"] = MakeData("1550", 2)
        };

        SMatrixOverrideApplicator.ApplyAll(new[] { comp1, comp2 }, store);

        comp1.WaveLengthToSMatrixMap.ShouldContainKey(1550);
        // comp2's wavelength map should remain the same (no matching key in store)
        comp2.WaveLengthToSMatrixMap.Keys.ToHashSet().ShouldBe(comp2OriginalKeys);
    }

    [Fact]
    public void Apply_WithPortNamesThatMatchPhysicalPins_Succeeds()
    {
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        // Physical pins are named "in" and "out"
        var data = new ComponentSMatrixData();
        data.Wavelengths["1550"] = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 0.9, 0.1, 0.1, 0.9 },
            Imag = new List<double>(new double[4]),
            PortNames = new List<string> { "in", "out" }
        };

        var applied = SMatrixOverrideApplicator.Apply(component, data);

        applied.ShouldBeTrue();
        component.WaveLengthToSMatrixMap.ShouldContainKey(1550);
    }
}

using System.IO;
using System.Numerics;
using System.Text.Json;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests;

namespace UnitTests.Import;

/// <summary>
/// Round-trip test for imported S-matrices through the .lun PIR serializer.
/// Exercises <see cref="SParameterConverter"/> plus <see cref="DesignFileData"/>
/// JSON save/load to verify a <c>StoredSMatrices</c> entry survives a
/// serialize → deserialize cycle without data loss. Guards against a silent
/// JSON-attribute regression that would drop <c>designData.SMatrices</c> at
/// save time — the only mechanism that made the whole import feature useful.
/// </summary>
public class SParameterPersistenceRoundTripTests
{
    [Fact]
    public void ImportedSMatrix_SurvivesDesignFileDataJsonRoundTrip()
    {
        // Build a realistic ImportedSParameters with asymmetric values at
        // three wavelengths to detect re-ordering / key-loss on round-trip.
        var imported = new ImportedSParameters
        {
            SourceFormat = "Test",
            SourceFilePath = "/irrelevant.sparam",
            PortCount = 2,
            PortNames = new List<string> { "port 1", "port 2" },
        };
        foreach (var wl in new[] { 1500, 1550, 1600 })
        {
            var m = new Complex[2, 2];
            m[0, 0] = new Complex(0.1 * wl, 0);
            m[0, 1] = new Complex(0.9 * wl, 0);
            m[1, 0] = new Complex(0.8 * wl, 0);
            m[1, 1] = new Complex(0.2 * wl, 0);
            imported.SMatricesByWavelengthNm[wl] = m;
        }

        var smatrixData = SParameterConverter.ToComponentSMatrixData(imported);

        // Save → serialize via the exact shape FileOperationsViewModel uses.
        var designData = new DesignFileData
        {
            SMatrices = new Dictionary<string, ComponentSMatrixData> { ["test_cmp"] = smatrixData }
        };
        var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions { WriteIndented = true });

        // Load → deserialize.
        var reloaded = JsonSerializer.Deserialize<DesignFileData>(json);

        reloaded.ShouldNotBeNull();
        reloaded.SMatrices.ShouldNotBeNull();
        reloaded.SMatrices.ShouldContainKey("test_cmp");

        var entry = reloaded.SMatrices["test_cmp"];
        entry.Wavelengths.ShouldContainKey("1500");
        entry.Wavelengths.ShouldContainKey("1550");
        entry.Wavelengths.ShouldContainKey("1600");

        // Row-major layout with asymmetric data: verify the 1550 entry.
        var e1550 = entry.Wavelengths["1550"];
        e1550.Rows.ShouldBe(2);
        e1550.Cols.ShouldBe(2);
        e1550.Real.Count.ShouldBe(4);
        // row=0,col=0 → [0], row=0,col=1 → [1], row=1,col=0 → [2], row=1,col=1 → [3]
        e1550.Real[0].ShouldBe(0.1 * 1550, tolerance: 1e-6);
        e1550.Real[1].ShouldBe(0.9 * 1550, tolerance: 1e-6);
        e1550.Real[2].ShouldBe(0.8 * 1550, tolerance: 1e-6);
        e1550.Real[3].ShouldBe(0.2 * 1550, tolerance: 1e-6);
        e1550.PortNames.ShouldNotBeNull();
        e1550.PortNames!.ShouldBe(new[] { "port 1", "port 2" });
    }

    [Fact]
    public void SaveLoad_SMatrixOverride_AppliesToReloadedComponentWithCorrectConvention()
    {
        // End-to-end contract: a serialized override deserializes and ApplyAll
        // produces the same (InFlow, OutFlow) → value mapping that Apply does
        // on a freshly imported live SMatrix. Catches a regression where the
        // JSON layer drops dimensions, port names, or row-major ordering — any
        // of which would silently produce wrong physics on reload.
        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        component.Identifier = "comp_42";
        var pinIn = component.PhysicalPins[0].LogicalPin!;
        var pinOut = component.PhysicalPins[1].LogicalPin!;

        var data = new ComponentSMatrixData
        {
            Wavelengths =
            {
                ["1550"] = new SMatrixWavelengthEntry
                {
                    Rows = 2, Cols = 2,
                    Real = new List<double> { 1, 2, 3, 4 },
                    Imag = new List<double> { 0, 0, 0, 0 }
                }
            }
        };

        var designData = new DesignFileData
        {
            SMatrices = new Dictionary<string, ComponentSMatrixData> { ["comp_42"] = data }
        };
        var json = JsonSerializer.Serialize(designData);
        var reloaded = JsonSerializer.Deserialize<DesignFileData>(json)!;

        var result = SMatrixOverrideApplicator.ApplyAll(new[] { component }, reloaded.SMatrices!);

        result.PerComponent["comp_42"].Applied.ShouldBe(1);
        result.OrphanKeys.ShouldBeEmpty();
        var transfers = component.WaveLengthToSMatrixMap[1550].GetNonNullValues();
        transfers[(pinIn.IDInFlow, pinIn.IDOutFlow)].ShouldBe(new Complex(1, 0));
        transfers[(pinOut.IDInFlow, pinIn.IDOutFlow)].ShouldBe(new Complex(2, 0));
        transfers[(pinIn.IDInFlow, pinOut.IDOutFlow)].ShouldBe(new Complex(3, 0));
        transfers[(pinOut.IDInFlow, pinOut.IDOutFlow)].ShouldBe(new Complex(4, 0));
    }

    [Fact]
    public void ImportedSMatrix_SurvivesFullDiskRoundTrip()
    {
        // Same contract but through an actual File.WriteAllText / ReadAllText
        // cycle — catches UTF-8 / newline / encoding regressions.
        var data = new ComponentSMatrixData
        {
            SourceNote = "Disk round-trip test",
            Wavelengths =
            {
                ["1550"] = new SMatrixWavelengthEntry
                {
                    Rows = 2, Cols = 2,
                    PortNames = new List<string> { "p1", "p2" },
                    Real = new List<double> { 0.1, 0.9, 0.8, 0.2 },
                    Imag = new List<double> { 0.0, 0.0, 0.0, 0.0 },
                }
            }
        };
        var designData = new DesignFileData
        {
            SMatrices = new Dictionary<string, ComponentSMatrixData> { ["custom"] = data }
        };

        var path = Path.Combine(Path.GetTempPath(), $"sparam_disk_{Guid.NewGuid()}.lun");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(designData));
            var loaded = JsonSerializer.Deserialize<DesignFileData>(File.ReadAllText(path));

            loaded!.SMatrices.ShouldContainKey("custom");
            loaded.SMatrices["custom"].Wavelengths["1550"].Real[1].ShouldBe(0.9, tolerance: 1e-9);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

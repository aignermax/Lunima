using System.IO;
using System.Linq;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Verifies the CSV → <see cref="ProcessDefinition"/> importer against synthetic
/// foundry-shaped tables (NOT real proprietary PDK data). Mirrors the layout a
/// Nazca PDK ships: table_layers / table_xsections / table_parameters.
/// </summary>
public class PdkProcessCsvImporterTests : IDisposable
{
    private readonly string _dir;

    public PdkProcessCsvImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pdkproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        File.WriteAllText(Path.Combine(_dir, "table_layers.csv"),
            "layer_name,layer_name_foundry,layer,datatype,accuracy,origin,remark,field,description\n" +
            "WAVEGUIDE,WG,12,0,0.001,fab,,Light,Passive Waveguide\n" +
            "METAL-1,Met1,52,0,0.001,fab,,Light,Metal routing line 1\n" +
            ",,,,,,,,\n"); // blank row tolerated

        File.WriteAllText(Path.Combine(_dir, "table_xsections.csv"),
            "origin,xsection,xsection_foundry,stub,description\n" +
            "fab,E1700,E1700,E1700Stub,Optical waveguide\n" +
            "fab,MetalDC,DC,MetalDCstub,\"Metal DC lines, wide\"\n" +
            "lib,E1700Stub,,,helper stub — should be skipped\n");

        File.WriteAllText(Path.Combine(_dir, "table_parameters.csv"),
            "name,value,unit,xsection,recommended,origin\n" +
            "width,2,um,WAVEGUIDE,,\n" +
            "arc_E1700,150,um,E1700,250,\n" +
            "width_metal_DC,10,um,Metal,,\n" +
            "arc_metal_DC,10,um,Metal,,\n");
    }

    [Fact]
    public void Import_ParsesLayers_SkippingBlankRows()
    {
        var process = new PdkProcessCsvImporter().Import(_dir, "TestProc");

        process.Name.ShouldBe("TestProc");
        process.Layers.Count.ShouldBe(2);
        var wg = process.Layers.First(l => l.Name == "WAVEGUIDE");
        wg.Layer.ShouldBe(12);
        wg.Field.ShouldBe("Light");
    }

    [Fact]
    public void Import_ClassifiesXsections_AndSkipsStubs()
    {
        var process = new PdkProcessCsvImporter().Import(_dir);

        process.Xsections.Select(x => x.Name).ShouldBe(new[] { "E1700", "MetalDC" });
        process.Xsections.First(x => x.Name == "E1700").Kind.ShouldBe(XsectionKind.Optical);
        process.Xsections.First(x => x.Name == "MetalDC").Kind.ShouldBe(XsectionKind.Metal);
        // Quoted comma in the description must not split the field.
        process.Xsections.First(x => x.Name == "MetalDC").Description.ShouldBe("Metal DC lines, wide");
    }

    [Fact]
    public void Import_AppliesWidthAndBendRadiiFromParameters()
    {
        var process = new PdkProcessCsvImporter().Import(_dir);

        var e1700 = process.Xsections.First(x => x.Name == "E1700");
        e1700.WidthUm.ShouldBe(2);            // from "width" (optical default)
        e1700.MinRadiusUm.ShouldBe(150);      // from arc_E1700 value
        e1700.RecommendedRadiusUm.ShouldBe(250); // from arc_E1700 recommended

        var metal = process.Xsections.First(x => x.Name == "MetalDC");
        metal.WidthUm.ShouldBe(10);           // from width_metal_DC (Metal family)
        metal.MinRadiusUm.ShouldBe(10);       // from arc_metal_DC (Metal family)
    }

    [Fact]
    public void Import_MissingTables_ReturnsEmptySections()
    {
        var empty = Path.Combine(Path.GetTempPath(), "pdkproc-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var process = new PdkProcessCsvImporter().Import(empty);
            process.Layers.ShouldBeEmpty();
            process.Xsections.ShouldBeEmpty();
        }
        finally { Directory.Delete(empty, true); }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }
}

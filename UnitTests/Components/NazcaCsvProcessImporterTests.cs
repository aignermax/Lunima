using System.IO;
using System.Linq;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Verifies the Nazca-CSV → <see cref="ProcessDefinition"/> importer against
/// synthetic foundry-shaped tables (NOT real proprietary PDK data).
/// </summary>
public class NazcaCsvProcessImporterTests : IDisposable
{
    private readonly string _dir;

    public NazcaCsvProcessImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pdkproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        File.WriteAllText(Path.Combine(_dir, "table_layers.csv"),
            "layer_name,layer_name_foundry,layer,datatype,accuracy,origin,remark,field,description\n" +
            "WAVEGUIDE,WG,12,0,0.001,fab,,Light,Passive Waveguide\n" +
            "METAL-1,Met1,52,0,0.001,fab,,Light,Metal routing line 1\n" +
            ",,,,,,,,\n");

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
    public void CanImport_RequiresCsvWithSiblingLayersTable()
    {
        var importer = new NazcaCsvProcessImporter();
        importer.CanImport(Path.Combine(_dir, "table_layers.csv")).ShouldBeTrue();
        importer.CanImport(Path.Combine(_dir, "anything.yaml")).ShouldBeFalse();
    }

    [Fact]
    public void Import_ParsesLayers_SkippingBlankRows()
    {
        var process = new NazcaCsvProcessImporter().ImportDirectory(_dir, "TestProc");

        process.Name.ShouldBe("TestProc");
        process.Layers.Count.ShouldBe(2);
        var wg = process.Layers.First(l => l.Name == "WAVEGUIDE");
        wg.Layer.ShouldBe(12);
        wg.Field.ShouldBe("Light");
    }

    [Fact]
    public void Import_ClassifiesXsections_AndSkipsStubs()
    {
        var process = new NazcaCsvProcessImporter().ImportDirectory(_dir);

        process.Xsections.Select(x => x.Name).ShouldBe(new[] { "E1700", "MetalDC" });
        process.Xsections.First(x => x.Name == "E1700").Kind.ShouldBe(XsectionKind.Optical);
        process.Xsections.First(x => x.Name == "MetalDC").Kind.ShouldBe(XsectionKind.Metal);
        process.Xsections.First(x => x.Name == "MetalDC").Description.ShouldBe("Metal DC lines, wide");
    }

    [Fact]
    public void Import_AppliesWidthAndBendRadiiFromParameters()
    {
        var process = new NazcaCsvProcessImporter().ImportDirectory(_dir);

        var e1700 = process.Xsections.First(x => x.Name == "E1700");
        e1700.WidthUm.ShouldBe(2);
        e1700.MinRadiusUm.ShouldBe(150);
        e1700.RecommendedRadiusUm.ShouldBe(250);

        var metal = process.Xsections.First(x => x.Name == "MetalDC");
        metal.WidthUm.ShouldBe(10);
        metal.MinRadiusUm.ShouldBe(10);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }
}

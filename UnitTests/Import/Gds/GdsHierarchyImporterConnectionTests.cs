using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsHierarchyImporterFixture;

namespace UnitTests.Import.Gds;

/// <summary>Abutment ambiguity and top-level external port connections.</summary>
public class GdsHierarchyImporterConnectionTests
{
    [Fact]
    public async Task Explode_AmbiguousPinPartners_WarnsAndFirstMatchWins()
    {
        // src.out coincides with BOTH sink instances' "in" pins (30 nm apart,
        // within the 0.05 µm default tolerance).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("src", 0, 0)
                .SRef("sink", 10000, 0)
                .SRef("sink", 10000, 30)
            .EndCell()
            .WaveguideCell("src")
            .WaveguideCell("sink")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        var connection = result.Connections.ShouldHaveSingleItem();
        connection.A.InstanceIndex.ShouldBe(0);
        connection.A.PinName.ShouldBe("out");
        connection.B.InstanceIndex.ShouldBe(1); // first sink in placement order wins
        connection.B.PinName.ShouldBe("in");
        result.Warnings.ShouldContain(w => w.Contains("candidates") && w.Contains("src#0"));
    }

    [Fact]
    public async Task Explode_TopLevelLabels_BecomeExternalPortConnections()
    {
        // Circuit ports as top-level labels; the label at the internal abutment
        // must NOT steal the instance-to-instance connection (instances win).
        var library = await ReadLibraryAsync(GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("wgA", 0, 0)
                .SRef("wgB", 10000, 0)
                .Text(1, 10, "in0", 0, 2000)
                .Text(1, 10, "mid", 10000, 2000)
                .Text(1, 10, "out0", 20000, 2000)
            .EndCell()
            .WaveguideCell("wgA")
            .WaveguideCell("wgB")
            .EndLibrary()
            .ToArray());

        var result = await GdsHierarchyImporter.ImportAsync(library, "TOP", new GdsHierarchyImportOptions());

        result.Connections.Count.ShouldBe(3);

        var abutment = result.Connections.Where(c => c.A.PinName == "out" && c.B.PinName == "in").ShouldHaveSingleItem();
        abutment.A.InstanceIndex.ShouldBe(0);
        abutment.B.InstanceIndex.ShouldBe(1);

        var input = result.Connections.Where(c => c.B.PinName == "in0").ShouldHaveSingleItem();
        input.A.InstanceIndex.ShouldBe(0);
        input.A.PinName.ShouldBe("in");
        input.B.IsTopLevelPort.ShouldBeTrue();
        input.XUm.ShouldBe(0, Tolerance);
        input.YUm.ShouldBe(2, Tolerance);

        var output = result.Connections.Where(c => c.B.PinName == "out0").ShouldHaveSingleItem();
        output.A.InstanceIndex.ShouldBe(1);
        output.A.PinName.ShouldBe("out");
        output.B.IsTopLevelPort.ShouldBeTrue();

        // The "mid" port lost to the instance-to-instance pair (one partner per pin).
        result.Connections.ShouldNotContain(c => c.B.PinName == "mid");
    }
}

using System;
using System.IO;
using System.Linq;
using CAP_Core.Export.FoundryDrc;
using Shouldly;
using Xunit;

namespace UnitTests.Export.FoundryDrc;

/// <summary>Reading KLayout marker databases (.lyrdb) into rules and violation markers.</summary>
public class DrcMarkerDatabaseTests
{
    private const string Report = """
        <?xml version="1.0" encoding="utf-8"?>
        <report-database>
         <categories>
          <category><name>'min_width'</name><description>Waveguide narrower than 0.3 um</description><categories/></category>
          <category><name>'min_space'</name><description>Gap below 1.5 um</description><categories/></category>
         </categories>
         <items>
          <item><category>'min_width'</category><cell>TOP</cell><values><value>polygon: (0,0;10,0;10,0.2;0,0.2)</value></values></item>
          <item><category>'min_width'</category><cell>TOP</cell><values><value>edge-pair: (100,-50;110,-50)|(100,-49.8;110,-49.8)</value></values></item>
         </items>
        </report-database>
        """;

    [Fact]
    public void Parse_CountsViolationsPerRule_IncludingCleanRules()
    {
        var (rules, markers) = DrcMarkerDatabase.Parse(Report);

        rules.Single(r => r.Rule == "min_width").Violations.ShouldBe(2);
        rules.Single(r => r.Rule == "min_space").Violations.ShouldBe(0, "clean rules are reported too");
        rules.Single(r => r.Rule == "min_width").Description.ShouldContain("0.3 um");
        markers.Count.ShouldBe(2);
    }

    [Fact]
    public void Parse_GivesEachMarkerItsBoundingBox()
    {
        var (_, markers) = DrcMarkerDatabase.Parse(Report);

        var edgePair = markers[1];
        (edgePair.MinX, edgePair.MaxX).ShouldBe((100, 110));
        (edgePair.MinY, edgePair.MaxY).ShouldBe((-50, -49.8));
        edgePair.Cell.ShouldBe("TOP");
    }

    [Fact]
    public void Parse_RejectsACorruptReport() =>
        Should.Throw<FormatException>(() => DrcMarkerDatabase.Parse("<report-database><items>"));

    [Fact]
    public async System.Threading.Tasks.Task Runner_ExplainsAMissingInput()
    {
        var result = await new KLayoutDrcRunner().RunAsync("/no/such.gds", "/no/deck.lydrc");

        result.Ran.ShouldBeFalse();
        result.Error.ShouldContain("GDS not found");
    }
}

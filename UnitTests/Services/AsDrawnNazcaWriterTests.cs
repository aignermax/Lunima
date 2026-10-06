using System.Text;
using CAP.Avalonia.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

public class AsDrawnNazcaWriterTests
{
    [Fact]
    public void Append_WritesEachPolygonVerbatimOnItsLayer_InNazcaCoordinates()
    {
        var geometry = new AsDrawnGeometry(new[]
        {
            new OutlinePolygon
            {
                Layer = 401,
                DataType = 3,
                Points = new[]
                {
                    new OutlinePoint(13113.099, 1038), new OutlinePoint(13620, 1038),
                    new OutlinePoint(13620, 1040), new OutlinePoint(13113.099, 1040), new OutlinePoint(13113.099, 1038),
                },
            },
        });
        var sb = new StringBuilder();

        AsDrawnNazcaWriter.Append(sb, geometry);

        sb.ToString().Trim().ShouldBe(
            "nd.Polygon(points=[(13113.099,-1038),(13620,-1038),(13620,-1040),(13113.099,-1040)], layer=(401, 3)).put(0, 0)",
            "Y is negated into Nazca space, the closing vertex is dropped, nm precision is kept");
    }

    [Fact]
    public void Append_SkipsDegeneratePolygons()
    {
        var geometry = new AsDrawnGeometry(new[]
        {
            new OutlinePolygon { Layer = 1, Points = new[] { new OutlinePoint(0, 0), new OutlinePoint(1, 0), new OutlinePoint(0, 0) } },
        });
        var sb = new StringBuilder();

        AsDrawnNazcaWriter.Append(sb, geometry);

        sb.Length.ShouldBe(0);
    }
}

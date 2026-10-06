using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Persistence;

public class AsDrawnPolygonDtoTests
{
    [Fact]
    public void RoundTrip_KeepsLayerAndVerticesOnTheNanometreGrid()
    {
        var polygon = new OutlinePolygon
        {
            Layer = 401,
            DataType = 2,
            Points = new[]
            {
                new OutlinePoint(13620, 1040), new OutlinePoint(13113.099, 1040),
                new OutlinePoint(13113.099, 1038), new OutlinePoint(-5.5, -0.001), new OutlinePoint(13620, 1040),
            },
        };

        var dtos = AsDrawnPolygonDto.FromGeometry(new AsDrawnGeometry(new[] { polygon }))!;
        var restored = AsDrawnPolygonDto.ToGeometry(dtos).ShouldNotBeNull();

        var back = restored.Polygons.ShouldHaveSingleItem();
        back.Layer.ShouldBe(401);
        back.DataType.ShouldBe(2);
        back.Points.Count.ShouldBe(5);
        for (int i = 0; i < 5; i++)
        {
            back.Points[i].X.ShouldBe(polygon.Points[i].X, 1e-9);
            back.Points[i].Y.ShouldBe(polygon.Points[i].Y, 1e-9);
        }
    }

    [Fact]
    public void Encoding_IsCompactForNeighbouringVertices()
    {
        var points = Enumerable.Range(0, 100).Select(i => new OutlinePoint(10000 + i * 0.5, 2000)).ToList();

        var encoded = PolygonPointCodec.Encode(points);

        encoded.Length.ShouldBeLessThan(points.Count * 6,
            "small deltas take a couple of bytes, not a decimal string per coordinate");
    }

    [Fact]
    public void NullOrEmpty_RestoresToNull()
    {
        AsDrawnPolygonDto.ToGeometry(null).ShouldBeNull();
        AsDrawnPolygonDto.ToGeometry(new List<AsDrawnPolygonDto>()).ShouldBeNull();
        AsDrawnPolygonDto.FromGeometry(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("gA==")] // a varint that never terminates
    public void MalformedPoints_AreSkipped(string points)
    {
        var dtos = new List<AsDrawnPolygonDto> { new() { Layer = 1, Points = points } };

        AsDrawnPolygonDto.ToGeometry(dtos).ShouldBeNull();
    }
}

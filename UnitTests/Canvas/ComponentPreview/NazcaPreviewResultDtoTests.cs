// UnitTests/Canvas/ComponentPreview/NazcaPreviewResultDtoTests.cs
using System.Collections.Generic;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class NazcaPreviewResultDtoTests
{
    [Fact]
    public void Roundtrip_PreservesPolygonsAndBbox()
    {
        var original = new NazcaPreviewResult
        {
            Success = true,
            XMin = -1, YMin = -2, XMax = 3, YMax = 4,
            Polygons = new List<NazcaPreviewPolygon>
            {
                new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (1, 0), (1, 1) } },
                new() { Layer = 7, Vertices = new List<(double, double)> { (2, 2), (3, 3) } },
            }
        };

        var json = NazcaPreviewResultDto.Serialize(original);
        var restored = NazcaPreviewResultDto.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.XMin.ShouldBe(-1); restored.YMax.ShouldBe(4);
        restored.Polygons.Count.ShouldBe(2);
        restored.Polygons[0].Layer.ShouldBe(1);
        restored.Polygons[0].Vertices.Count.ShouldBe(3);
        restored.Polygons[0].Vertices[2].ShouldBe((1.0, 1.0));
        restored.Polygons[1].Layer.ShouldBe(7);
    }

    [Fact]
    public void Deserialize_GarbageReturnsNull()
    {
        NazcaPreviewResultDto.Deserialize("{ not valid json").ShouldBeNull();
        NazcaPreviewResultDto.Deserialize("").ShouldBeNull();
    }
}

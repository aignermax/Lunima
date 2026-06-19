// UnitTests/Canvas/ComponentPreview/GdsPreviewDiskCacheTests.cs
using System.Collections.Generic;
using System.IO;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class GdsPreviewDiskCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunima-gdscache-test-" + Guid.NewGuid().ToString("N"));

    private NazcaPreviewResult SampleResult() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 5,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (10, 0), (10, 5) } }
        }
    };

    [Fact]
    public void WriteThenRead_ReturnsHit_WithSamePolygons()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");

        cache.Write(key, SampleResult());
        cache.TryRead(key, out var result).ShouldBeTrue();

        result.ShouldNotBeNull();
        result!.Polygons.Count.ShouldBe(1);
        result.Polygons[0].Vertices.Count.ShouldBe(3);
    }

    [Fact]
    public void WriteEmpty_ReadReturnsHitWithNull_NoRetry()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");

        cache.WriteEmpty(key);
        cache.TryRead(key, out var result).ShouldBeTrue(); // present...
        result.ShouldBeNull();                              // ...but empty
    }

    [Fact]
    public void TryRead_MissReturnsFalse()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        cache.TryRead(new GdsPreviewKey("x", "y", "z"), out _).ShouldBeFalse();
    }

    [Fact]
    public void TryRead_CorruptFileTreatedAsMiss()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");
        cache.Write(key, SampleResult());
        File.WriteAllText(Path.Combine(_dir, key.Hash() + ".json"), "{ corrupt");

        cache.TryRead(key, out _).ShouldBeFalse();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}

using CAP.Avalonia.Services.Solvers;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers.Fdtd;

/// <summary>
/// Verifies the geometry/port mapping the FDTD request factory applies to a
/// Nazca preview render (layer filtering and pin → port mapping).
/// </summary>
public class ComponentFdtdRequestFactoryTests
{
    private static NazcaPreviewPolygon Poly(int layer) => new()
    {
        Layer = layer,
        Vertices = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0) },
    };

    [Fact]
    public void BuildPolygons_KeepsOnlyOpticalLayer()
    {
        var polys = new[] { Poly(1), Poly(1), Poly(20), Poly(1003) };

        var result = ComponentFdtdRequestFactory.BuildPolygons(polys, siliconLayer: 1);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(p => p.Layer == 1);
    }

    [Fact]
    public void BuildPolygons_FallsBackToAllLayers_WhenNoneMatch()
    {
        var polys = new[] { Poly(2), Poly(501) };

        var result = ComponentFdtdRequestFactory.BuildPolygons(polys, siliconLayer: 1);

        result.Count.ShouldBe(2); // no layer-1 polygons → keep everything rather than render nothing
    }

    [Fact]
    public void BuildPorts_UsesComponentPinNames_KeepsPreviewPositions()
    {
        var pins = new[]
        {
            new NazcaPreviewPin { Name = "a0", X = 0, Y = 0, Angle = 180 },
            new NazcaPreviewPin { Name = "b0", X = 80, Y = 2, Angle = 0 },
        };

        // Component pin names differ from the Nazca cell pin names — these must win,
        // matched by index, while positions/angles stay from the preview.
        var ports = ComponentFdtdRequestFactory.BuildPorts(pins, new[] { "port 1", "port 2" }, portWidthUm: 2.0);

        ports.Count.ShouldBe(2);
        ports[0].Name.ShouldBe("port 1");
        ports[0].Orientation.ShouldBe(180);
        ports[0].X.ShouldBe(0);
        ports[1].Name.ShouldBe("port 2");
        ports[1].X.ShouldBe(80);
        ports[1].Width.ShouldBe(2.0);
    }

    [Fact]
    public void BuildPorts_FallsBackToPreviewNames_OnCountMismatch()
    {
        var pins = new[]
        {
            new NazcaPreviewPin { Name = "a0", X = 0, Y = 0, Angle = 180 },
            new NazcaPreviewPin { Name = "b0", X = 80, Y = 2, Angle = 0 },
        };

        // Only one component name for two pins → can't safely match → keep preview names.
        var ports = ComponentFdtdRequestFactory.BuildPorts(pins, new[] { "port 1" }, portWidthUm: 2.0);

        ports[0].Name.ShouldBe("a0");
        ports[1].Name.ShouldBe("b0");
    }
}

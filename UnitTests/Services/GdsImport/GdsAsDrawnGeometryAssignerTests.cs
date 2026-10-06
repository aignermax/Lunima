using CAP.Avalonia.Services.GdsImport.FrozenRoutes;
using CAP_Core.Routing.ImportedGeometry;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Routing.ImportedGeometry;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Layout tools draw a waveguide on several layers (core, cladding, trench, pin
/// markers); the assigner must hand every such polygon to the route it belongs to and
/// leave unrelated shapes alone.
/// </summary>
public class GdsAsDrawnGeometryAssignerTests
{
    private static GdsOutlinePolygon Polygon(int layer, IEnumerable<(double X, double Y)> points) => new()
    {
        Layer = layer,
        Points = points.Select(p => new GdsOutlinePoint(p.X, p.Y)).ToList(),
    };

    private static (GdsAsDrawnGeometryAssigner Assigner, int Owner) OwnerWithCoreStraight()
    {
        var assigner = new GdsAsDrawnGeometryAssigner();
        var core = RibbonTestPolygons.Straight(0, 0, 100, 0, 2);
        var fit = RibbonCenterlineFitter.Fit(core)!;
        int owner = assigner.AddOwner(new[] { fit }, new[] { GdsAsDrawnGeometryAssigner.ToOutline(Polygon(401, core), 0, 0) });
        return (assigner, owner);
    }

    [Fact]
    public void CladdingRibbonWithTheSameCenterline_JoinsTheRoute()
    {
        var (assigner, owner) = OwnerWithCoreStraight();

        var unassigned = assigner.Assign(new[] { Polygon(404, RibbonTestPolygons.Straight(0, 0, 100, 0, 8)) }, 0, 0);

        unassigned.ShouldBeEmpty();
        assigner.PolygonsOf(owner).Select(p => p.Layer).ShouldBe(new[] { 401, 404 });
    }

    [Fact]
    public void ShortWideCladdingRectangle_IsMatchedAlongItsOtherAxis()
    {
        // A 10 µm straight on a 40 µm wide trench layer: its "caps" are the long edges.
        var assigner = new GdsAsDrawnGeometryAssigner();
        var core = RibbonTestPolygons.Straight(0, 0, 10, 0, 2);
        int owner = assigner.AddOwner(new[] { RibbonCenterlineFitter.Fit(core)! },
            new[] { GdsAsDrawnGeometryAssigner.ToOutline(Polygon(401, core), 0, 0) });

        var unassigned = assigner.Assign(new[] { Polygon(18, RibbonTestPolygons.Straight(0, 0, 10, 0, 40)) }, 0, 0);

        unassigned.ShouldBeEmpty();
        assigner.PolygonsOf(owner).Count.ShouldBe(2);
    }

    [Fact]
    public void SmallMarkerAtAPieceEnd_JoinsTheRoute()
    {
        var (assigner, owner) = OwnerWithCoreStraight();
        var arrow = new[] { (99.5, -0.5), (100.5, 0.0), (99.5, 0.5), (99.5, -0.5) };

        var unassigned = assigner.Assign(new[] { Polygon(234, arrow) }, 0, 0);

        unassigned.ShouldBeEmpty();
        assigner.PolygonsOf(owner).ShouldContain(p => p.Layer == 234);
    }

    [Fact]
    public void UnrelatedShapes_StayUnassigned()
    {
        var (assigner, owner) = OwnerWithCoreStraight();
        var logo = new[] { (500.0, 500.0), (800.0, 500.0), (650.0, 700.0), (500.0, 500.0) };
        var farMarker = new[] { (300.0, 300.0), (301.0, 300.0), (301.0, 301.0), (300.0, 300.0) };

        var unassigned = assigner.Assign(new[] { Polygon(24, logo), Polygon(234, farMarker) }, 0, 0);

        unassigned.Count.ShouldBe(2);
        assigner.PolygonsOf(owner).ShouldHaveSingleItem();
    }

    [Fact]
    public void Assign_AppliesThePlanToCanvasOffset()
    {
        var assigner = new GdsAsDrawnGeometryAssigner();
        var coreCanvas = RibbonTestPolygons.Straight(50, 20, 150, 20, 2);
        int owner = assigner.AddOwner(new[] { RibbonCenterlineFitter.Fit(coreCanvas)! }, Array.Empty<CAP_Core.Components.Core.OutlinePolygon>());

        // The cladding is given in plan space; the import shifted everything by (50, 20).
        var unassigned = assigner.Assign(new[] { Polygon(404, RibbonTestPolygons.Straight(0, 0, 100, 0, 8)) }, 50, 20);

        unassigned.ShouldBeEmpty();
        assigner.PolygonsOf(owner).ShouldHaveSingleItem().Points.Min(p => p.X).ShouldBe(50, 1e-9);
    }
}

using CAP_Core.Routing;
using CAP_Core.Routing.ImportedGeometry;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.ImportedGeometry;

public class RibbonCenterlineFitterTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void Fit_AxisAlignedRectangle_ReturnsExactStraight()
    {
        var outline = new List<(double X, double Y)> { (0, 0), (100, 0), (100, 2), (0, 2) };

        var fit = RibbonCenterlineFitter.Fit(outline).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Straight);
        fit.WidthMicrometers.ShouldBe(2.0, Tolerance);
        fit.Segments.Count.ShouldBe(1);
        var ends = new[] { fit.Start, fit.End }.OrderBy(p => p.X).ToArray();
        ends[0].X.ShouldBe(0, Tolerance);
        ends[0].Y.ShouldBe(1, Tolerance);
        ends[1].X.ShouldBe(100, Tolerance);
        ends[1].Y.ShouldBe(1, Tolerance);
        fit.Segments[0].LengthMicrometers.ShouldBe(100, Tolerance);
    }

    [Fact]
    public void Fit_ClosedOutlineWithRepeatedFirstPoint_IgnoresClosingVertex()
    {
        var outline = new List<(double X, double Y)> { (0, 0), (50, 0), (50, 4), (0, 4), (0, 0) };

        var fit = RibbonCenterlineFitter.Fit(outline).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Straight);
        fit.WidthMicrometers.ShouldBe(4.0, Tolerance);
    }

    [Fact]
    public void Fit_DiagonalStraight_RecoversAngleAndLength()
    {
        var outline = RibbonTestPolygons.Straight(10, 10, 40, 50, 1.5);

        var fit = RibbonCenterlineFitter.Fit(outline).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Straight);
        fit.Segments[0].LengthMicrometers.ShouldBe(50, 1e-6);
        fit.WidthMicrometers.ShouldBe(1.5, 1e-6);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(0, -90)]
    [InlineData(45, 180)]
    [InlineData(270, -45)]
    public void Fit_ArcRibbon_ReturnsExactBend(double startAngle, double sweep)
    {
        var expected = new BendSegment(500, -300, 250, startAngle, sweep);
        var outline = RibbonTestPolygons.Arc(expected, 2.0);

        var fit = RibbonCenterlineFitter.Fit(outline).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Arc);
        fit.WidthMicrometers.ShouldBe(2.0, 1e-4);
        var bend = fit.Segments.Single().ShouldBeOfType<BendSegment>();
        bend.RadiusMicrometers.ShouldBe(250, 1e-4);
        bend.Center.X.ShouldBe(500, 1e-4);
        bend.Center.Y.ShouldBe(-300, 1e-4);
        Math.Abs(bend.SweepAngleDegrees).ShouldBe(Math.Abs(sweep), 1e-3);
        bend.LengthMicrometers.ShouldBe(expected.LengthMicrometers, 1e-2);
        AssertSameEndpoints(fit, expected.StartPoint, expected.EndPoint);
    }

    [Fact]
    public void Fit_ArcRibbon_SegmentStartsWhereTheFitSays()
    {
        var expected = new BendSegment(0, 0, 50, 0, 90);
        var fit = RibbonCenterlineFitter.Fit(RibbonTestPolygons.Arc(expected, 2.0)).ShouldNotBeNull();

        var bend = (BendSegment)fit.Segments[0];
        var reconstructed = new BendSegment(bend.Center.X, bend.Center.Y, bend.RadiusMicrometers,
            bend.StartAngleDegrees, bend.SweepAngleDegrees);

        reconstructed.StartPoint.X.ShouldBe(fit.Start.X, 1e-6);
        reconstructed.StartPoint.Y.ShouldBe(fit.Start.Y, 1e-6);
        reconstructed.EndPoint.X.ShouldBe(fit.End.X, 1e-6);
        reconstructed.EndPoint.Y.ShouldBe(fit.End.Y, 1e-6);
    }

    [Fact]
    public void Fit_ArcWithCompensatedInteriorVertices_IsStillAnExactBend()
    {
        // Layout tools put a discretized arc's interior vertices on a slightly larger
        // circle than its end vertices (measured on a production file: +25 nm inside,
        // −38 nm at the caps for R = 250 µm). The fit must see one circle, not a polyline.
        const double radius = 250, width = 2, interiorShift = 0.025, endShift = -0.038;
        var outline = CompensatedArc(radius, width, interiorShift, endShift, samples: 31);

        var fit = RibbonCenterlineFitter.Fit(outline).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Arc);
        var bend = (BendSegment)fit.Segments.Single();
        bend.Center.X.ShouldBe(0, 1e-3);
        bend.Center.Y.ShouldBe(0, 1e-3);
        bend.RadiusMicrometers.ShouldBe(radius + endShift, 1e-6, "the radius follows the exact cap midpoints");
        Math.Abs(bend.SweepAngleDegrees).ShouldBe(90, 1e-6);
        fit.WidthMicrometers.ShouldBe(width, 1e-9);
    }

    /// <summary>A 90° ribbon around the origin whose interior/end vertices are shifted radially.</summary>
    private static List<(double X, double Y)> CompensatedArc(
        double radius, double width, double interiorShift, double endShift, int samples)
    {
        List<(double X, double Y)> Side(double r)
        {
            var side = new List<(double X, double Y)>();
            for (int i = 0; i < samples; i++)
            {
                double phi = Math.PI / 2 * i / (samples - 1);
                double rr = r + (i == 0 || i == samples - 1 ? endShift : interiorShift);
                side.Add((rr * Math.Cos(phi), rr * Math.Sin(phi)));
            }
            return side;
        }
        var outer = Side(radius + width / 2);
        var inner = Side(radius - width / 2);
        inner.Reverse();
        return outer.Concat(inner).ToList();
    }

    [Fact]
    public void Fit_NonCircularCurve_FallsBackToPolylineThroughMidpoints()
    {
        // A clothoid-like curve: curvature grows linearly along the path.
        var centerline = new List<(double X, double Y, double Angle)>();
        double x = 0, y = 0, angle = 0;
        const double step = 1.0;
        for (int i = 0; i <= 60; i++)
        {
            centerline.Add((x, y, angle));
            angle += 0.05 * i * step;
            x += step * Math.Cos(angle * Math.PI / 180.0);
            y += step * Math.Sin(angle * Math.PI / 180.0);
        }

        var fit = RibbonCenterlineFitter.Fit(RibbonTestPolygons.FromCenterline(centerline, 2.0)).ShouldNotBeNull();

        fit.Kind.ShouldBe(RibbonFitKind.Polyline);
        fit.WidthMicrometers.ShouldBe(2.0, 0.05);
        fit.Segments.Sum(s => s.LengthMicrometers).ShouldBe(60.0, 0.5);
        AssertSameEndpoints(fit, (centerline[0].X, centerline[0].Y), (centerline[^1].X, centerline[^1].Y), 1e-6);
    }

    [Fact]
    public void Fit_Triangle_ReturnsNull()
    {
        RibbonCenterlineFitter.Fit(new List<(double X, double Y)> { (0, 0), (10, 0), (5, 5) }).ShouldBeNull();
    }

    [Fact]
    public void Reversed_SwapsEndpointsAndKeepsLength()
    {
        var fit = RibbonCenterlineFitter.Fit(RibbonTestPolygons.Arc(new BendSegment(0, 0, 30, 0, 90), 2.0)).ShouldNotBeNull();

        var reversed = fit.Reversed();

        reversed.Start.X.ShouldBe(fit.End.X, 1e-6);
        reversed.Start.Y.ShouldBe(fit.End.Y, 1e-6);
        reversed.End.X.ShouldBe(fit.Start.X, 1e-6);
        reversed.End.Y.ShouldBe(fit.Start.Y, 1e-6);
        reversed.Segments.Sum(s => s.LengthMicrometers).ShouldBe(fit.Segments.Sum(s => s.LengthMicrometers), 1e-9);
    }

    private static void AssertSameEndpoints(RibbonFit fit, (double X, double Y) a, (double X, double Y) b, double tol = 1e-3)
    {
        bool forward = Distance(fit.Start, a) < tol && Distance(fit.End, b) < tol;
        bool backward = Distance(fit.Start, b) < tol && Distance(fit.End, a) < tol;
        (forward || backward).ShouldBeTrue($"fit {fit.Start}->{fit.End} vs expected {a}->{b}");
    }

    private static double Distance((double X, double Y) p, (double X, double Y) q) =>
        Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
}

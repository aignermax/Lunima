using OxyPlot;
using OxyPlot.Series;

namespace CAP.Avalonia.Controls.Plotting;

/// <summary>
/// A <see cref="LineSeries"/> whose tracker reports the value at the cursor's
/// X position (wavelength), linearly interpolated along the line, instead of the
/// Euclidean-nearest data point. This lets the user read values off the chart by
/// X alone — moving the mouse left/right — without having to follow the curve
/// vertically. The Y distance is ignored when locating the point; OxyPlot still
/// picks the closest series in Y, so the tracker shows "the value of the curve
/// you are pointing at, at the X you clicked".
/// </summary>
public class XTrackingLineSeries : LineSeries
{
    /// <summary>
    /// Returns the point on the line at the cursor's X coordinate. The
    /// <paramref name="interpolate"/> flag is intentionally ignored: this series
    /// always interpolates along X so tracking behaves consistently whether the
    /// controller is bound to Track or SnapTrack.
    /// </summary>
    public override TrackerHitResult GetNearestPoint(ScreenPoint point, bool interpolate)
    {
        var points = ActualPoints;
        if (points == null || points.Count == 0)
            return null!;

        double targetX = InverseTransform(point).X;

        if (targetX <= points[0].X)
            return Hit(points[0], 0);
        if (targetX >= points[^1].X)
            return Hit(points[^1], points.Count - 1);

        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (targetX < a.X || targetX > b.X)
                continue;

            double span = b.X - a.X;
            double t = span == 0 ? 0 : (targetX - a.X) / span;
            double y = a.Y + t * (b.Y - a.Y);
            return Hit(new DataPoint(targetX, y), i);
        }

        return Hit(points[^1], points.Count - 1);
    }

    private TrackerHitResult Hit(DataPoint dataPoint, int index) => new()
    {
        Series = this,
        DataPoint = dataPoint,
        Position = Transform(dataPoint),
        Index = index,
        Text = $"{Title}\nλ = {dataPoint.X:0} nm\nIL = {dataPoint.Y:0.00} dB",
    };
}

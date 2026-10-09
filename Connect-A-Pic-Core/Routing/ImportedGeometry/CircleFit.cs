namespace CAP_Core.Routing.ImportedGeometry;

/// <summary>A least-squares circle through a point set.</summary>
/// <param name="CenterX">Circle center X (µm).</param>
/// <param name="CenterY">Circle center Y (µm).</param>
/// <param name="Radius">Circle radius (µm).</param>
/// <param name="MaxResidual">Largest |distance-to-center − radius| over the fitted points (µm).</param>
public readonly record struct CircleFit(double CenterX, double CenterY, double Radius, double MaxResidual)
{
    /// <summary>
    /// Algebraic (Kåsa) circle fit, computed relative to the centroid for numerical
    /// stability at chip-scale coordinates. Returns null for fewer than three points
    /// or (near-)collinear input.
    /// </summary>
    /// <param name="points">Points expected to lie on one circle.</param>
    public static CircleFit? Fit(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3) return null;
        double meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
        foreach (var (x, y) in points)
        {
            double u = x - meanX, v = y - meanY;
            suu += u * u; svv += v * v; suv += u * v;
            suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
        }

        double det = suu * svv - suv * suv;
        if (Math.Abs(det) < 1e-12 * Math.Max(1.0, suu * svv)) return null;
        double rhsU = 0.5 * (suuu + suvv), rhsV = 0.5 * (svvv + svuu);
        double uc = (rhsU * svv - rhsV * suv) / det;
        double vc = (suu * rhsV - suv * rhsU) / det;
        double cx = uc + meanX, cy = vc + meanY;
        double radius = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / points.Count);
        double residual = points.Max(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - radius));
        return new CircleFit(cx, cy, radius, residual);
    }
}

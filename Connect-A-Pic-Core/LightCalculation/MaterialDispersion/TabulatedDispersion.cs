namespace CAP_Core.LightCalculation.MaterialDispersion;

/// <summary>
/// Tabulated dispersion model that interpolates between measured data points.
/// Uses linear interpolation for each quantity; outside the defined range the
/// nearest endpoint value is returned (clamping — no extrapolation).
/// </summary>
public sealed class TabulatedDispersion : IDispersionModel
{
    private readonly (double WavelengthNm, double Value)[] _nEffPoints;
    private readonly (double WavelengthNm, double Value)[] _groupIndexPoints;
    private readonly (double WavelengthNm, double Value)[] _lossPoints;

    /// <summary>
    /// Creates a tabulated dispersion model.
    /// </summary>
    /// <param name="nEffPoints">
    /// Pairs of (wavelengthNm, nEff). Must contain at least one point.
    /// </param>
    /// <param name="groupIndexPoints">
    /// Pairs of (wavelengthNm, n_g). When null or empty, group index is
    /// estimated from the nEff gradient at each evaluation wavelength.
    /// </param>
    /// <param name="lossPoints">
    /// Pairs of (wavelengthNm, lossDbPerCm). Must contain at least one point.
    /// </param>
    public TabulatedDispersion(
        IEnumerable<(double WavelengthNm, double Value)> nEffPoints,
        IEnumerable<(double WavelengthNm, double Value)>? groupIndexPoints,
        IEnumerable<(double WavelengthNm, double Value)> lossPoints)
    {
        _nEffPoints = Sort(nEffPoints, nameof(nEffPoints));
        _lossPoints = Sort(lossPoints, nameof(lossPoints));

        var ngList = groupIndexPoints?.ToList();
        _groupIndexPoints = (ngList != null && ngList.Count > 0)
            ? Sort(ngList, nameof(groupIndexPoints))
            : Array.Empty<(double, double)>();
    }

    /// <inheritdoc/>
    public double NEffAt(double wavelengthNm)
        => Interpolate(_nEffPoints, wavelengthNm);

    /// <inheritdoc/>
    public double GroupIndexAt(double wavelengthNm)
    {
        if (_groupIndexPoints.Length > 0)
            return Interpolate(_groupIndexPoints, wavelengthNm);

        // Estimate from nEff slope: n_g = n_eff - λ·dn_eff/dλ
        double nEff = NEffAt(wavelengthNm);
        double slope = NEffSlope(wavelengthNm);
        return nEff - wavelengthNm * slope;
    }

    /// <inheritdoc/>
    public double LossDbPerCmAt(double wavelengthNm)
        => Math.Max(0.0, Interpolate(_lossPoints, wavelengthNm));

    // ---- helpers ----

    private static (double, double)[] Sort(
        IEnumerable<(double WavelengthNm, double Value)> points,
        string paramName)
    {
        var arr = points.ToArray();
        if (arr.Length == 0)
            throw new ArgumentException($"'{paramName}' must contain at least one point.", paramName);
        Array.Sort(arr, (a, b) => a.WavelengthNm.CompareTo(b.WavelengthNm));
        return arr;
    }

    private static double Interpolate(
        (double WavelengthNm, double Value)[] points,
        double wavelengthNm)
    {
        if (points.Length == 1)
            return points[0].Value;

        // Clamp to range
        if (wavelengthNm <= points[0].WavelengthNm)
            return points[0].Value;
        if (wavelengthNm >= points[^1].WavelengthNm)
            return points[^1].Value;

        // Binary search for the bracketing interval
        int lo = 0, hi = points.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid + 1].WavelengthNm <= wavelengthNm)
                lo = mid + 1;
            else
                hi = mid;
        }

        double wl0 = points[lo].WavelengthNm;
        double wl1 = points[lo + 1].WavelengthNm;
        double t = (wavelengthNm - wl0) / (wl1 - wl0);
        return points[lo].Value + t * (points[lo + 1].Value - points[lo].Value);
    }

    /// <summary>
    /// Numerical slope of n_eff at the given wavelength using the tabulated data.
    /// </summary>
    private double NEffSlope(double wavelengthNm)
    {
        if (_nEffPoints.Length < 2)
            return 0.0;

        // Use the nearest two points for a finite-difference estimate
        if (wavelengthNm <= _nEffPoints[0].WavelengthNm)
        {
            double dWl = _nEffPoints[1].WavelengthNm - _nEffPoints[0].WavelengthNm;
            return dWl == 0 ? 0.0 : (_nEffPoints[1].Value - _nEffPoints[0].Value) / dWl;
        }
        if (wavelengthNm >= _nEffPoints[^1].WavelengthNm)
        {
            int last = _nEffPoints.Length - 1;
            double dWl = _nEffPoints[last].WavelengthNm - _nEffPoints[last - 1].WavelengthNm;
            return dWl == 0 ? 0.0 : (_nEffPoints[last].Value - _nEffPoints[last - 1].Value) / dWl;
        }

        // Central difference using the bracketing points
        int lo = 0;
        while (lo < _nEffPoints.Length - 2 && _nEffPoints[lo + 1].WavelengthNm < wavelengthNm)
            lo++;

        double wl0 = _nEffPoints[lo].WavelengthNm;
        double wl1 = _nEffPoints[lo + 1].WavelengthNm;
        double dWlBracket = wl1 - wl0;
        return dWlBracket == 0 ? 0.0 : (_nEffPoints[lo + 1].Value - _nEffPoints[lo].Value) / dWlBracket;
    }
}

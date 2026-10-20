namespace CAP_DataAccess.Import.Gds;

internal sealed partial class GdsHierarchyImportSession
{
    private IReadOnlyList<DetectedPin>? GetReviewedPins(string cellName)
    {
        var pins = _options.Interpretation?.Cells.FirstOrDefault(c => c.CellName == cellName)?.Pins;
        if (pins is null)
            return null;
        var bbox = cellName == _topCellName ? TopBBox : GetCellBBox(cellName);
        const double portBoundsToleranceUm = 1.0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in pins)
        {
            if (string.IsNullOrWhiteSpace(pin.Name) || !names.Add(pin.Name))
                throw new InvalidDataException($"Cell '{cellName}' requires unique, nonempty port names.");
            if (pin.XUm < -portBoundsToleranceUm || pin.XUm > bbox.Width + portBoundsToleranceUm
                || pin.YUm < -portBoundsToleranceUm || pin.YUm > bbox.Height + portBoundsToleranceUm)
                throw new InvalidDataException($"Cell '{cellName}' port '{pin.Name}' lies outside its geometry bounds.");
            if (!double.IsFinite(pin.XUm) || !double.IsFinite(pin.YUm)
                || !double.IsFinite(pin.AngleDegrees) || !double.IsFinite(pin.WidthUm) || pin.WidthUm < 0)
                throw new InvalidDataException($"Cell '{cellName}' contains an invalid port coordinate, angle or width.");
        }
        return pins;
    }
}

namespace CAP_DataAccess.Import.Gds.Interpretation;

/// <summary>Expands reviewed hierarchy containers without mutating the parsed source library.</summary>
public static class GdsInterpretationExpander
{
    /// <summary>Moves expanded containers' own drawn geometry to the top cell and retains child references.</summary>
    public static GdsLibrary Expand(GdsLibrary source, GdsImportInterpretation interpretation)
    {
        var expanded = interpretation.Cells.Where(c => c.ExpandChildren)
            .Select(c => c.CellName).ToHashSet(StringComparer.Ordinal);
        if (expanded.Count == 0)
            return source;
        var result = new GdsLibrary
        {
            Name = source.Name,
            UserUnitsPerDatabaseUnit = source.UserUnitsPerDatabaseUnit,
            DatabaseUnitInMeters = source.DatabaseUnitInMeters,
        };
        foreach (var pair in source.Cells)
            result.Cells.Add(pair.Key, pair.Value);
        var top = new GdsCell { Name = interpretation.TopCellName };
        result.Cells[top.Name] = top;
        var sourceTop = source.Cells[top.Name];
        top.Elements.AddRange(sourceTop.Elements.Where(e => e is not GdsReference));
        foreach (var reference in sourceTop.Elements.OfType<GdsReference>())
            VisitReference(reference, GdsTransform.Identity, new HashSet<string>(StringComparer.Ordinal));
        return result;

        void VisitReference(GdsReference reference, GdsTransform outer, HashSet<string> path)
        {
            for (var row = 0; row < reference.Rows; row++)
                for (var column = 0; column < reference.Columns; column++)
                {
                    var transform = GdsTransform.FromReference(reference, column, row).Then(outer);
                    if (!expanded.Contains(reference.CellName))
                    {
                        top.Elements.Add(ToReference(reference.CellName, transform));
                        continue;
                    }
                    if (!path.Add(reference.CellName))
                        throw new InvalidDataException($"Cyclic hierarchy at '{reference.CellName}'.");
                    var cell = source.Cells[reference.CellName];
                    if (!cell.Elements.OfType<GdsReference>().Any())
                        throw new InvalidDataException($"Cell '{cell.Name}' has no child cells to split.");
                    foreach (var polygon in GdsPathOutliner.ExpandDrawnGeometry(cell.Elements))
                        top.Elements.Add(polygon with { Points = polygon.Points.Select(transform.Apply).ToArray() });
                    // Internal labels remain in the source bytes; promoting them would create external ports.
                    foreach (var child in cell.Elements.OfType<GdsReference>())
                        VisitReference(child, transform, path);
                    path.Remove(reference.CellName);
                }
        }
    }

    private static GdsReference ToReference(string cellName, GdsTransform transform) => new()
    {
        CellName = cellName,
        Offset = new GdsPoint(transform.C, transform.F),
        AngleDegrees = Math.Atan2(transform.D, transform.A) * 180 / Math.PI,
        Magnification = Math.Sqrt(transform.A * transform.A + transform.D * transform.D),
        Reflected = transform.A * transform.E - transform.B * transform.D < 0,
    };
}

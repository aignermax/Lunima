namespace CAP_DataAccess.Import.Gds.LayerPicker;

/// <summary>A (layer, datatype) pair identifying one GDS layer.</summary>
public readonly record struct GdsLayerPair(int Layer, int Datatype)
{
    /// <summary>Renders the pair in the census notation, e.g. <c>(56,0)</c>.</summary>
    public override string ToString() => $"({Layer},{Datatype})";
}

/// <summary>
/// All flattened geometry of one (layer, datatype) pair of a top cell:
/// polygons (paths already expanded to outlines by the flattener) plus text
/// anchors. Coordinates are micrometers, GDS Y-up.
/// </summary>
public sealed record GdsLayerGeometry
{
    /// <summary>The (layer, datatype) pair this geometry belongs to.</summary>
    public GdsLayerPair Pair { get; init; }

    /// <summary>Flattened polygons on this pair, in top-cell coordinates.</summary>
    public IReadOnlyList<GdsPolygon> Polygons { get; init; } = Array.Empty<GdsPolygon>();

    /// <summary>Flattened texts on this pair (pair uses the texttype), in top-cell coordinates.</summary>
    public IReadOnlyList<GdsText> Texts { get; init; } = Array.Empty<GdsText>();
}

/// <summary>
/// The click-to-assign picker's render/hit-test model: the selected top cell's
/// flattened geometry grouped per (layer, datatype) pair, plus the overall
/// bounding box for the view transform. Built once per picker session from the
/// already-parsed library — no re-read of the file.
/// </summary>
public sealed record GdsLayerGeometryScene
{
    /// <summary>Name of the flattened top cell.</summary>
    public string CellName { get; init; } = string.Empty;

    /// <summary>Per-pair geometry groups, sorted by layer then datatype.</summary>
    public IReadOnlyList<GdsLayerGeometry> Layers { get; init; } = Array.Empty<GdsLayerGeometry>();

    /// <summary>Bounding box over all polygons and text anchors, micrometers, Y-up.</summary>
    public GdsBoundingBox Bounds { get; init; }

    /// <summary>
    /// Flattens <paramref name="topCellName"/> and groups its geometry by
    /// (layer, datatype). Texts group by their texttype so the pairs line up
    /// with the layer census and the port-layer field syntax.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Unknown cell, reference to an undefined cell, or a reference cycle.
    /// </exception>
    public static GdsLayerGeometryScene Build(GdsLibrary library, string topCellName)
    {
        ArgumentNullException.ThrowIfNull(library);
        var flattened = new GdsCellFlattener(library).Flatten(topCellName);

        var polygonGroups = flattened.Polygons
            .GroupBy(p => new GdsLayerPair(p.Layer, p.DataType))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GdsPolygon>)g.ToList());
        var textGroups = flattened.Texts
            .GroupBy(t => new GdsLayerPair(t.Layer, t.TextType))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GdsText>)g.ToList());

        var layers = polygonGroups.Keys.Union(textGroups.Keys)
            .OrderBy(pair => pair.Layer).ThenBy(pair => pair.Datatype)
            .Select(pair => new GdsLayerGeometry
            {
                Pair = pair,
                Polygons = polygonGroups.GetValueOrDefault(pair) ?? Array.Empty<GdsPolygon>(),
                Texts = textGroups.GetValueOrDefault(pair) ?? Array.Empty<GdsText>(),
            })
            .ToList();

        return new GdsLayerGeometryScene
        {
            CellName = topCellName,
            Layers = layers,
            Bounds = ComputeBounds(layers),
        };
    }

    private static GdsBoundingBox ComputeBounds(IReadOnlyList<GdsLayerGeometry> layers)
    {
        var points = layers
            .SelectMany(l => l.Polygons.SelectMany(p => p.Points)
                .Concat(l.Texts.Select(t => t.Position)));
        GdsBoundingBox? box = null;
        foreach (var point in points)
        {
            box = box is null
                ? new GdsBoundingBox(point.X, point.Y, point.X, point.Y)
                : box.Value.Include(point);
        }
        return box ?? GdsBoundingBox.Empty;
    }
}

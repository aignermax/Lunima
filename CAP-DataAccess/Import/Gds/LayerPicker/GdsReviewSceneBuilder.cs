namespace CAP_DataAccess.Import.Gds.LayerPicker;

/// <summary>
/// Builds bounded review previews without expanding the entire hierarchy.
/// The source library must remain unchanged for the lifetime of this builder.
/// Bounds are exact under the importer's point/path-width convention and cached
/// per source cell and linear transform; array translations need only their extremes.
/// </summary>
public sealed class GdsReviewSceneBuilder
{
    private const int PolygonBudget = 20_000;
    private const int VertexBudget = 250_000;
    private const int TextBudget = 5_000;
    private const int NodeBudget = 100_000;
    private readonly GdsLibrary _library;
    private readonly Dictionary<BoundsKey, GdsBoundingBox?> _bounds = new();
    private readonly object _boundsGate = new();
    private readonly record struct BoundsKey(string CellName, GdsTransform Linear);

    public GdsReviewSceneBuilder(GdsLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
    }

    public GdsBoundingBox GetBounds(string cellName, CancellationToken cancellationToken = default) =>
        Bounds(cellName, GdsTransform.Identity, new HashSet<string>(StringComparer.Ordinal), cancellationToken)
        ?? GdsBoundingBox.Empty;

    public GdsLayerGeometryScene Build(string cellName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = GetBounds(cellName, cancellationToken);
        var layers = new Dictionary<GdsLayerPair, LayerData>();
        CollectLayers(cellName, layers, new HashSet<string>(StringComparer.Ordinal), cancellationToken);
        var budget = new Budget();
        Sample(cellName, GdsTransform.Identity, layers, budget, cancellationToken);
        return new GdsLayerGeometryScene
        {
            CellName = cellName,
            Bounds = bounds,
            IsLimited = budget.Limited,
            Layers = layers.OrderBy(x => x.Key.Layer).ThenBy(x => x.Key.Datatype)
                .Select(x => new GdsLayerGeometry
                {
                    Pair = x.Key,
                    Polygons = x.Value.Polygons.ToArray(),
                    Texts = x.Value.Texts.ToArray(),
                }).ToArray(),
        };
    }

    private GdsBoundingBox? Bounds(
        string name, GdsTransform linear, HashSet<string> active, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = new BoundsKey(name, linear);
        lock (_boundsGate)
            if (_bounds.TryGetValue(key, out var cached))
                return cached;
        if (!active.Add(name))
            throw new InvalidDataException($"Cyclic GDS cell reference detected at '{name}'.");
        try
        {
            GdsBoundingBox? box = null;
            foreach (var element in Cell(name).Elements)
            {
                ct.ThrowIfCancellationRequested();
                switch (element)
                {
                    case GdsPolygon polygon:
                        foreach (var point in polygon.Points)
                        {
                            ct.ThrowIfCancellationRequested();
                            Include(ref box, linear.Apply(point));
                        }
                        break;
                    case GdsText text:
                        Include(ref box, linear.Apply(text.Position));
                        break;
                    case GdsPath path:
                        double scale = Math.Sqrt(linear.A * linear.A + linear.D * linear.D);
                        double halfWidth = path.WidthMicrometers * scale / 2;
                        foreach (var point in path.Points)
                        {
                            ct.ThrowIfCancellationRequested();
                            var p = linear.Apply(point);
                            Include(ref box, new(p.X - halfWidth, p.Y - halfWidth));
                            Include(ref box, new(p.X + halfWidth, p.Y + halfWidth));
                        }
                        break;
                    case GdsReference reference when reference.Columns > 0 && reference.Rows > 0:
                        var transform = GdsTransform.FromReference(reference, 0, 0).Then(linear);
                        var childLinear = transform with { C = 0, F = 0 };
                        var child = Bounds(reference.CellName, childLinear, active, ct);
                        if (child is not { } childBox)
                            break;
                        // Translation is affine in the two array indices, so all
                        // extrema occur at a corner, even under rotation/reflection.
                        foreach (int row in new[] { 0, reference.Rows - 1 }.Distinct())
                            foreach (int column in new[] { 0, reference.Columns - 1 }.Distinct())
                            {
                                ct.ThrowIfCancellationRequested();
                                var placement = GdsTransform.FromReference(reference, column, row).Then(linear);
                                Include(ref box, new(childBox.MinX + placement.C, childBox.MinY + placement.F));
                                Include(ref box, new(childBox.MaxX + placement.C, childBox.MaxY + placement.F));
                            }
                        break;
                }
            }
            lock (_boundsGate)
                _bounds[key] = box;
            return box;
        }
        finally
        {
            active.Remove(name);
        }
    }

    private void CollectLayers(
        string name, Dictionary<GdsLayerPair, LayerData> layers, HashSet<string> visited, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!visited.Add(name))
            return;
        foreach (var element in Cell(name).Elements)
        {
            ct.ThrowIfCancellationRequested();
            GdsLayerPair? pair = element switch
            {
                GdsPolygon p => new(p.Layer, p.DataType),
                GdsPath p => new(p.Layer, p.DataType),
                GdsText t => new(t.Layer, t.TextType),
                _ => null,
            };
            if (pair is { } value)
                layers.TryAdd(value, new LayerData());
            else if (element is GdsReference { Columns: > 0, Rows: > 0 } reference)
                CollectLayers(reference.CellName, layers, visited, ct);
        }
    }

    private void Sample(
        string name, GdsTransform transform, Dictionary<GdsLayerPair, LayerData> layers,
        Budget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (budget.Limited)
            return;
        if (++budget.Nodes > NodeBudget)
        {
            budget.Limited = true;
            return;
        }
        foreach (var element in Cell(name).Elements)
        {
            ct.ThrowIfCancellationRequested();
            if (budget.Limited)
                return;
            switch (element)
            {
                case GdsPolygon polygon:
                    AddPolygon(polygon, transform, layers, budget, ct);
                    break;
                case GdsText text:
                    if (budget.Texts == TextBudget)
                    {
                        budget.Limited = true;
                        return;
                    }
                    double angle = Math.Atan2(transform.D, transform.A) * 180 / Math.PI;
                    bool mirrored = transform.A * transform.E - transform.B * transform.D < 0;
                    layers[new(text.Layer, text.TextType)].Texts.Add(text with
                    {
                        Position = transform.Apply(text.Position),
                        AngleDegrees = angle + (mirrored ? -text.AngleDegrees : text.AngleDegrees),
                    });
                    budget.Texts++;
                    break;
                case GdsPath path:
                    AddPath(path, transform, layers, budget, ct);
                    break;
                case GdsReference reference:
                    for (int row = 0; row < reference.Rows && !budget.Limited; row++)
                        for (int column = 0; column < reference.Columns && !budget.Limited; column++)
                        {
                            ct.ThrowIfCancellationRequested();
                            Sample(reference.CellName,
                                GdsTransform.FromReference(reference, column, row).Then(transform), layers, budget, ct);
                        }
                    break;
            }
        }
    }

    private static void AddPolygon(
        GdsPolygon polygon, GdsTransform transform, Dictionary<GdsLayerPair, LayerData> layers,
        Budget budget, CancellationToken ct)
    {
        if (budget.Polygons == PolygonBudget || polygon.Points.Count > VertexBudget - budget.Vertices)
        {
            budget.Limited = true;
            return;
        }
        var points = new GdsPoint[polygon.Points.Count];
        for (int i = 0; i < points.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            points[i] = transform.Apply(polygon.Points[i]);
        }
        layers[new(polygon.Layer, polygon.DataType)].Polygons.Add(polygon with { Points = points });
        budget.Polygons++;
        budget.Vertices += points.Length;
    }

    private static void AddPath(
        GdsPath path, GdsTransform transform, Dictionary<GdsLayerPair, LayerData> layers,
        Budget budget, CancellationToken ct)
    {
        if (path.WidthMicrometers <= 0 || path.Points.Count == 0)
            return;
        // Bound input before Outline allocates its cleaned centerline and quads.
        int capacity = Math.Min(PolygonBudget - budget.Polygons, (VertexBudget - budget.Vertices) / 5);
        if (capacity <= 0)
        {
            budget.Limited = true;
            return;
        }
        int count = Math.Min(path.Points.Count, capacity + 1);
        var prefix = new GdsPoint[count];
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            prefix[i] = path.Points[i];
        }
        foreach (var polygon in GdsPathOutliner.Outline(path with { Points = prefix }))
        {
            ct.ThrowIfCancellationRequested();
            AddPolygon(polygon, transform, layers, budget, ct);
            if (budget.Limited)
                return;
        }
        budget.Limited = count < path.Points.Count;
    }

    private GdsCell Cell(string name) => _library.Cells.TryGetValue(name, out var cell)
        ? cell : throw new InvalidDataException($"GDS cell '{name}' is referenced but not defined in the library.");

    private static void Include(ref GdsBoundingBox? box, GdsPoint point) =>
        box = box is { } existing ? existing.Include(point) : new(point.X, point.Y, point.X, point.Y);

    private sealed class LayerData
    {
        public List<GdsPolygon> Polygons { get; } = new();
        public List<GdsText> Texts { get; } = new();
    }

    private sealed class Budget
    {
        public int Polygons;
        public int Vertices;
        public int Texts;
        public int Nodes;
        public bool Limited;
    }
}

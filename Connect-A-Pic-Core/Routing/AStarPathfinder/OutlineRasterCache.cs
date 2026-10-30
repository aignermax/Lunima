using System.Collections.Immutable;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

/// <summary>
/// Grid-owned LRU of detached scan-conversion values. Entries contain no component,
/// pin, cell buffer or callback references; worker snapshots may share their read-only rows.
/// Both entry count and conservative retained-byte accounting bound cache ownership.
/// </summary>
internal sealed class OutlineRasterCache
{
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private readonly Dictionary<Guid, LinkedListNode<OutlineRasterEntry>> _entries = new();
    private readonly LinkedList<OutlineRasterEntry> _recent = new();
    private HashSet<Guid>? _rebuildUses;
    internal int Count => _entries.Count;
    internal long RetainedBytes { get; private set; }
    internal int RasterizationCount { get; private set; }

    internal OutlineRasterCache(int maximumEntries = 256, long maximumBytes = 32 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumEntries = maximumEntries;
        _maximumBytes = maximumBytes;
    }

    internal (OutlineRasterRows Body, OutlineRasterRows Padded) GetOrBuild(Guid id,
        IReadOnlyList<OutlinePoint[]> polygons, PathfindingGrid grid, double padding)
    {
        _rebuildUses?.Add(id);
        var key = RasterGridKey.From(grid, padding);
        if (_entries.TryGetValue(id, out var node) && node.Value.Matches(key, polygons))
        {
            _recent.Remove(node);
            _recent.AddLast(node);
            return (node.Value.Body, node.Value.Padded);
        }
        RasterizationCount += 2;
        var body = OutlineRasterRows.Build(polygons, grid, 0);
        var padded = OutlineRasterRows.Build(polygons, grid, padding);
        // Copy caller arrays exactly once. Hashes alone never authorize a cache hit.
        var points = RasterPolygonValues.Capture(polygons);
        var bytes = 256L + points.RetainedBytes + body.RetainedBytes + padded.RetainedBytes;
        Retain(new(id, key, points, body, padded, bytes));
        return (body, padded);
    }

    internal ImmutableArray<OutlineRasterEntry> Capture() => _recent.ToImmutableArray();

    internal void Seed(ImmutableArray<OutlineRasterEntry> entries)
    {
        if (entries.IsDefault) return;
        foreach (var entry in entries) Retain(entry);
    }

    internal void BeginRebuild() => _rebuildUses = new();
    internal void EndRebuild()
    {
        if (_rebuildUses is not { } used) return;
        _rebuildUses = null;
        foreach (var id in _entries.Keys.Where(id => !used.Contains(id)).ToArray()) Remove(id);
    }

    internal void Remove(Component component)
    {
        Remove(component.Id);
        if (component is ComponentGroup group)
            foreach (var child in group.ChildComponents) Remove(child);
    }

    internal void Remove(Guid id)
    {
        if (!_entries.Remove(id, out var node)) return;
        _recent.Remove(node);
        RetainedBytes -= node.Value.Bytes;
    }

    private void Retain(OutlineRasterEntry entry)
    {
        Remove(entry.Id);
        if (entry.Bytes > _maximumBytes) return;
        while (_entries.Count >= _maximumEntries || RetainedBytes + entry.Bytes > _maximumBytes)
        {
            Remove(_recent.First!.Value.Id);
        }
        _entries.Add(entry.Id, _recent.AddLast(entry));
        RetainedBytes += entry.Bytes;
    }
}

internal readonly record struct RasterGridKey(double MinX, double MinY, double MaxX, double MaxY,
    int Width, int Height, double CellSize, double Padding)
{
    internal static RasterGridKey From(PathfindingGrid grid, double padding) => new(grid.MinX, grid.MinY,
        grid.MaxX, grid.MaxY, grid.Width, grid.Height, grid.CellSizeMicrometers, padding);
}

/// <summary>All geometry is world-space and detached, including each polygon's complete vertex sequence.</summary>
internal sealed record OutlineRasterEntry(Guid Id, RasterGridKey Grid,
    RasterPolygonValues Polygons, OutlineRasterRows Body, OutlineRasterRows Padded, long Bytes)
{
    internal bool Matches(RasterGridKey grid, IReadOnlyList<OutlinePoint[]> polygons) =>
        Grid == grid && Polygons.Matches(polygons);
}

/// <summary>
/// Packed immutable vertices plus every polygon boundary. Many small GDS polygons do
/// not need a separate retained array/object per ring; equal flattened vertices alone
/// are insufficient because a different partition describes different edges and fills.
/// </summary>
internal sealed class RasterPolygonValues
{
    private readonly ImmutableArray<OutlinePoint> _vertices;
    private readonly ImmutableArray<int> _offsets;
    private RasterPolygonValues(ImmutableArray<OutlinePoint> vertices, ImmutableArray<int> offsets)
    {
        _vertices = vertices;
        _offsets = offsets;
    }
    internal long RetainedBytes => 96L + _vertices.Length * 16L + _offsets.Length * 4L;

    internal static RasterPolygonValues Capture(IReadOnlyList<OutlinePoint[]> polygons)
    {
        var count = polygons.Sum(polygon => polygon.Length);
        var vertices = ImmutableArray.CreateBuilder<OutlinePoint>(count);
        var offsets = ImmutableArray.CreateBuilder<int>(polygons.Count + 1);
        offsets.Add(0);
        foreach (var polygon in polygons)
        {
            vertices.AddRange(polygon);
            offsets.Add(vertices.Count);
        }
        return new(vertices.MoveToImmutable(), offsets.MoveToImmutable());
    }

    internal bool Matches(IReadOnlyList<OutlinePoint[]> polygons)
    {
        if (_offsets.Length != polygons.Count + 1) return false;
        for (var index = 0; index < polygons.Count; index++)
        {
            var length = _offsets[index + 1] - _offsets[index];
            if (length != polygons[index].Length
                || !_vertices.AsSpan(_offsets[index], length).SequenceEqual(polygons[index])) return false;
        }
        return true;
    }
}

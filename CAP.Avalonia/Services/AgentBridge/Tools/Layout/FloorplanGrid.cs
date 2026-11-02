using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Layout;

/// <summary>An axis-aligned rectangle in µm (top-left, size).</summary>
public sealed record Area(double X, double Y, double Width, double Height)
{
    /// <summary>Area in µm².</summary>
    public double Size => Width * Height;
}

/// <summary>
/// A coarse occupancy raster of the chip: a cell is taken when a component's bounds (grown by
/// a clearance) or a routed waveguide passes through it. Answers which large rectangles are
/// still free — the floorplanning view an agent needs before placing a block.
/// </summary>
public sealed class FloorplanGrid
{
    private const int TargetCellsPerSide = 96;
    private const int SamplesPerCell = 2;

    private readonly bool[,] _taken;
    private readonly double _originX;
    private readonly double _originY;

    private FloorplanGrid(double originX, double originY, double cell, int columns, int rows)
    {
        (_originX, _originY, Cell) = (originX, originY, cell);
        (Columns, Rows) = (columns, rows);
        _taken = new bool[columns, rows];
    }

    /// <summary>Cell edge in µm.</summary>
    public double Cell { get; }

    /// <summary>Number of columns.</summary>
    public int Columns { get; }

    /// <summary>Number of rows.</summary>
    public int Rows { get; }

    /// <summary>Rasterises the chip area of <paramref name="canvas"/> with <paramref name="clearance"/> around parts.</summary>
    public static FloorplanGrid Build(DesignCanvasViewModel canvas, double clearance)
    {
        var width = canvas.ChipMaxX - canvas.ChipMinX;
        var height = canvas.ChipMaxY - canvas.ChipMinY;
        var cell = Math.Max(1, Math.Max(width, height) / TargetCellsPerSide);
        var grid = new FloorplanGrid(canvas.ChipMinX, canvas.ChipMinY, cell,
            (int)Math.Ceiling(width / cell), (int)Math.Ceiling(height / cell));

        foreach (var vm in canvas.Components)
        {
            var b = DesignDescriber.Describe(vm).Bounds;
            grid.MarkRect(b.MinX - clearance, b.MinY - clearance, b.MaxX + clearance, b.MaxY + clearance);
        }
        foreach (var wire in canvas.Connections.Select(c => c.Connection.RoutedPath).Where(p => p != null))
            foreach (var segment in wire!.Segments)
                grid.MarkLine(segment.StartPoint, segment.EndPoint);
        return grid;
    }

    /// <summary>Fraction of cells taken.</summary>
    public double TakenFraction()
    {
        var taken = 0;
        foreach (var cell in _taken)
            if (cell) taken++;
        return Columns * Rows == 0 ? 0 : taken / (double)(Columns * Rows);
    }

    /// <summary>
    /// The <paramref name="count"/> largest free rectangles, greedily: the largest, then the
    /// largest that does not overlap it, and so on.
    /// </summary>
    public IReadOnlyList<Area> LargestFree(int count)
    {
        var blocked = (bool[,])_taken.Clone();
        var result = new List<Area>();
        for (var i = 0; i < count; i++)
        {
            var best = LargestFreeCells(blocked);
            if (best == null) break;
            var (c0, r0, c1, r1) = best.Value;
            for (var c = c0; c <= c1; c++)
                for (var r = r0; r <= r1; r++)
                    blocked[c, r] = true;
            result.Add(new Area(Math.Round(_originX + c0 * Cell, 2), Math.Round(_originY + r0 * Cell, 2),
                Math.Round((c1 - c0 + 1) * Cell, 2), Math.Round((r1 - r0 + 1) * Cell, 2)));
        }
        return result;
    }

    /// <summary>Largest all-free cell rectangle (maximal rectangle in a binary matrix, histogram method).</summary>
    private (int C0, int R0, int C1, int R1)? LargestFreeCells(bool[,] blocked)
    {
        var heights = new int[Columns];
        (int C0, int R0, int C1, int R1)? best = null;
        var bestArea = 0;
        for (var r = 0; r < Rows; r++)
        {
            for (var c = 0; c < Columns; c++)
                heights[c] = blocked[c, r] ? 0 : heights[c] + 1;
            var stack = new Stack<int>();
            for (var c = 0; c <= Columns; c++)
            {
                var h = c == Columns ? 0 : heights[c];
                while (stack.Count > 0 && heights[stack.Peek()] >= h)
                {
                    var height = heights[stack.Pop()];
                    var left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                    var area = height * (c - left);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = (left, r - height + 1, c - 1, r);
                    }
                }
                stack.Push(c);
            }
        }
        return bestArea > 0 ? best : null;
    }

    private void MarkRect(double minX, double minY, double maxX, double maxY)
    {
        var (c0, r0) = CellOf(minX, minY);
        var (c1, r1) = CellOf(maxX, maxY);
        for (var c = Math.Max(0, c0); c <= Math.Min(Columns - 1, c1); c++)
            for (var r = Math.Max(0, r0); r <= Math.Min(Rows - 1, r1); r++)
                _taken[c, r] = true;
    }

    private void MarkLine((double X, double Y) from, (double X, double Y) to)
    {
        var length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        var steps = Math.Max(1, (int)Math.Ceiling(length / Cell * SamplesPerCell));
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var (c, r) = CellOf(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            if (c >= 0 && c < Columns && r >= 0 && r < Rows)
                _taken[c, r] = true;
        }
    }

    private (int Column, int Row) CellOf(double x, double y) =>
        ((int)Math.Floor((x - _originX) / Cell), (int)Math.Floor((y - _originY) / Cell));
}

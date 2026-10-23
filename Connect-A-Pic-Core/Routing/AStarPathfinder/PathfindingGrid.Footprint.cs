using System.Collections;
using CAP_Core.Components.Core;

namespace CAP_Core.Routing.AStarPathfinder;

public partial class PathfindingGrid
{
    /// <summary>
    /// The cells one component blocks, described instead of enumerated: its padded
    /// rectangle minus its carved pin corridors, clipped to the grid, with the body
    /// rectangle telling body cells from padding cells. A full-chip import holds
    /// components spanning millions of cells; storing one hash-set entry (and one
    /// ownership record) per cell made every grid rebuild take seconds.
    /// Enumerating yields exactly the cells the rasterization marked.
    /// </summary>
    private sealed class ComponentFootprint : IEnumerable<(int x, int y)>
    {
        private readonly int _width;
        private readonly int _height;
        private readonly OutlineRasterRows? _outlineBody;
        private readonly OutlineRasterRows? _outlinePadded;

        public ComponentFootprint(
            Component owner,
            (int X1, int Y1, int X2, int Y2) padded,
            (int X1, int Y1, int X2, int Y2) body,
            HashSet<(int, int)> corridorCells,
            int gridWidth,
            int gridHeight,
            OutlineRasterRows? outlineBody = null,
            OutlineRasterRows? outlinePadded = null)
        {
            Owner = owner;
            Padded = padded;
            Body = body;
            CorridorCells = corridorCells;
            _width = gridWidth;
            _height = gridHeight;
            _outlineBody = outlineBody;
            _outlinePadded = outlinePadded;
        }

        /// <summary>The component that blocks these cells.</summary>
        public Component Owner { get; }

        internal ComponentFootprint WithOwner(Component owner) => new(owner, Padded, Body, CorridorCells,
            _width, _height, _outlineBody, _outlinePadded);

        /// <summary>Padded rectangle in grid cells (inclusive, unclipped).</summary>
        public (int X1, int Y1, int X2, int Y2) Padded { get; }

        /// <summary>Body rectangle in grid cells (inclusive).</summary>
        public (int X1, int Y1, int X2, int Y2) Body { get; }

        /// <summary>Pin-corridor cells left open inside the padded rectangle.</summary>
        public HashSet<(int, int)> CorridorCells { get; }

        /// <summary>True when the component blocks the cell; <paramref name="isBody"/> tells body from padding.</summary>
        public bool Claims(int x, int y, out bool isBody)
        {
            isBody = false;
            if (x < Padded.X1 || x > Padded.X2 || y < Padded.Y1 || y > Padded.Y2) return false;
            if (x < 0 || y < 0 || x >= _width || y >= _height) return false;
            if (CorridorCells.Contains((x, y))) return false;
            if (_outlinePadded is not null)
            {
                isBody = _outlineBody!.Contains(x, y);
                return _outlinePadded.Contains(x, y);
            }
            isBody = x >= Body.X1 && x <= Body.X2 && y >= Body.Y1 && y <= Body.Y2;
            return true;
        }

        /// <inheritdoc/>
        public IEnumerator<(int x, int y)> GetEnumerator()
        {
            if (_outlinePadded is not null)
            {
                foreach (var cell in _outlinePadded.Cells())
                    if (!CorridorCells.Contains(cell)) yield return cell;
                yield break;
            }
            int x1 = Math.Max(0, Padded.X1), x2 = Math.Min(_width - 1, Padded.X2);
            int y1 = Math.Max(0, Padded.Y1), y2 = Math.Min(_height - 1, Padded.Y2);
            for (int x = x1; x <= x2; x++)
            for (int y = y1; y <= y2; y++)
            {
                if (!CorridorCells.Contains((x, y)))
                    yield return (x, y);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

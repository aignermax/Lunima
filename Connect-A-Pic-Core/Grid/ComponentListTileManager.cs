using CAP_Core.Tiles;
using Component = CAP_Core.Components.Core.Component;

namespace CAP_Core.Grid;

/// <summary>
/// Lightweight ITileManager implementation that stores components in a list
/// instead of a tile grid. Used for physical-coordinate simulation mode
/// where tile positions are irrelevant.
/// </summary>
public class ComponentListTileManager : ITileManager
{
    private readonly List<Component> _components = new();

    // Dummy tile grid - not used in physical coordinate mode
    public Tile[,] Tiles { get; } = new Tile[1, 1];
    public int Width => 1;
    public int Height => 1;

    public ComponentListTileManager()
    {
        Tiles[0, 0] = new Tile(0, 0);
    }

    public bool IsCoordinatesInGrid(float x, float y, int width = 1, int height = 1) => true;

    public List<Component> GetAllComponents() => _components.ToList();

    public void AddComponent(Component component)
    {
        if (!_components.Contains(component))
            _components.Add(component);
    }

    public void Clear() => _components.Clear();
}

using CAP_Core.Tiles;
using ConnectAPic.LayoutWindow;
using Godot;
using System;

public class PhysicalToScreenAdapter
{
    // GameManager.TilePixelSize = 62 pixels
    // GridToMicrometerScale = 250 µm
    // => 62 pixels / 250 µm = 0.248 pixels/µm
    public double MicrometerToPixelScale { get; set; } = GameManager.TilePixelSize / Tile.GridToMicrometerScale;

    // Für Grid-Modus: GridSize in µm

    public double GridSizeInMicrometers => Tile.GridToMicrometerScale;

    public Vector2 PhysicalToScreen(double physicalX, double physicalY)
    {
        return new Vector2(
            (float)(physicalX * MicrometerToPixelScale),
            (float)(physicalY * MicrometerToPixelScale)
        );
    }

    public (double x, double y) ScreenToPhysical(Vector2 screenPos)
    {
        return (
            screenPos.X / MicrometerToPixelScale,
            screenPos.Y / MicrometerToPixelScale
        );
    }

    // Grid-Snapping im physikalischen Raum
    public (double x, double y) SnapToGrid(double physicalX, double physicalY)
    {
        var gridX = Math.Round(physicalX / GridSizeInMicrometers) * GridSizeInMicrometers;
        var gridY = Math.Round(physicalY / GridSizeInMicrometers) * GridSizeInMicrometers;
        return (gridX, gridY);
    }
    public (double x, double y) SnapToSubGrid(double physicalX, double physicalY, double snapSize = 10.0)
    {
        var snappedX = Math.Round(physicalX / snapSize) * snapSize;
        var snappedY = Math.Round(physicalY / snapSize) * snapSize;
        return (snappedX, snappedY);
    }
}

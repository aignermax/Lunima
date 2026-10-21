using Avalonia;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Commands;

public partial class UngroupCommand
{
    // Imported top-cell backgrounds belong to the group outline, not its paths.
    // Freeze their exact rendered pose before removing the group. Canvas frozen
    // masks remain render-only, are exported verbatim, and already serialize to .lun.
    private void ReleaseBackgroundOutlines()
    {
        if (_group.OutlinePolygons is not { Count: > 0 } polygons) return;
        var frame = GdsPolygonRenderer.GetUnrotatedDestRect(_group.PhysicalX, _group.PhysicalY,
            _group.WidthMicrometers, _group.HeightMicrometers, _group.RotationDegrees,
            _group.UnrotatedWidthMicrometers, _group.UnrotatedHeightMicrometers);
        var rotation = GdsPolygonRenderer.BuildRotationMatrix(_group.RotationDegrees,
            _group.PhysicalX + _group.WidthMicrometers / 2, _group.PhysicalY + _group.HeightMicrometers / 2);
        foreach (var polygon in polygons)
        {
            var world = polygon with { Points = polygon.Points.Select(point =>
            {
                var localY = _group.IsMirroredHorizontally ? frame.Height - point.Y : point.Y;
                var placed = rotation.Transform(new Point(frame.X + point.X, frame.Y + localY));
                return new OutlinePoint(placed.X, placed.Y);
            }).ToList() };
            var path = GdsFrozenRoutePathFactory.Create(new GdsOutlinePolygon
            {
                Layer = world.Layer, DataType = world.DataType,
                Points = world.Points.Select(point => new GdsOutlinePoint(point.X, point.Y)).ToList()
            });
            path.AsDrawnGeometry = new AsDrawnGeometry(new[] { world });
            var vm = new CanvasFrozenPathViewModel(path);
            _canvas.CanvasFrozenPaths.Add(vm);
            _releasedFrozenPathViewModels.Add(vm);
        }
    }
}

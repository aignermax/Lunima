using Avalonia;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Controls.Canvas.LengthLock;

/// <summary>Finds the length-lock padlock under the pointer.</summary>
internal static class LengthLockHitTest
{
    /// <summary>Grab tolerance around the drawn padlock, as a factor of its radius.</summary>
    private const double GrabFactor = 1.3;

    /// <summary>The length-locked route whose padlock is at <paramref name="canvasPoint"/>, or null.</summary>
    public static WaveguideConnectionViewModel? Hit(Point canvasPoint, DesignCanvasViewModel canvas, double zoom)
    {
        if (zoom < ComponentRenderer.GroupOverviewZoom)
            return null;
        double reach = LengthLockOverlay.BadgeRadiusWorld(zoom) * GrabFactor;
        foreach (var connection in canvas.Connections)
        {
            if (connection.Connection.TargetLengthMicrometers == null)
                continue;
            var segments = connection.Connection.GetPathSegments();
            if (segments.Count == 0)
                continue;
            var (x, y) = LengthLockOverlay.Midpoint(segments);
            double dx = canvasPoint.X - x, dy = canvasPoint.Y - y;
            if (dx * dx + dy * dy <= reach * reach)
                return connection;
        }
        return null;
    }
}

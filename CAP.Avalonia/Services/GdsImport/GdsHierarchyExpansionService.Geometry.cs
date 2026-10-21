using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

public sealed partial class GdsHierarchyExpansionService
{
    private static List<ComponentViewModel> CreateChildren(Component parent, GdsCircuitImport imported,
        Dictionary<string, ComponentTemplate> templates, double width, double height)
    {
        var children = new List<ComponentViewModel>();
        foreach (var instance in imported.Instances)
        {
            var template = templates[instance.CellName];
            var child = ComponentTemplates.CreateFromTemplate(template, 0, 0);
            var radians = instance.RotationDegrees * Math.PI / 180;
            var childWidth = template.WidthMicrometers * Math.Abs(Math.Cos(radians)) +
                template.HeightMicrometers * Math.Abs(Math.Sin(radians));
            var childHeight = template.WidthMicrometers * Math.Abs(Math.Sin(radians)) +
                template.HeightMicrometers * Math.Abs(Math.Cos(radians));
            var center = Transform(parent, instance.PositionXUm + childWidth / 2,
                instance.PositionYUm + childHeight / 2, width, height);
            var mirrored = parent.IsMirroredHorizontally ^ instance.Reflected;
            var angle = parent.RotationDegrees + (parent.IsMirroredHorizontally ? -1 : 1) * instance.RotationDegrees;
            if (mirrored) ComponentPoseTransform.MirrorPinsHorizontally(child);
            ComponentPoseTransform.ApplyExactRotation(child, angle);
            child.PhysicalX = center.X - child.WidthMicrometers / 2;
            child.PhysicalY = center.Y - child.HeightMicrometers / 2;
            child.IsRoutingObstacle = parent.IsRoutingObstacle;
            children.Add(new ComponentViewModel(child, template.Name, template.PdkSource));
        }
        return children;
    }

    private static (double X, double Y) Transform(Component parent, double x, double y, double width, double height)
    {
        x -= width / 2;
        y -= height / 2;
        if (parent.IsMirroredHorizontally) y = -y;
        var radians = parent.RotationDegrees * Math.PI / 180;
        return (parent.PhysicalX + parent.WidthMicrometers / 2 + x * Math.Cos(radians) - y * Math.Sin(radians),
            parent.PhysicalY + parent.HeightMicrometers / 2 + x * Math.Sin(radians) + y * Math.Cos(radians));
    }

    private static List<WaveguideConnectionViewModel> CreateConnections(GdsCircuitImport imported,
        IReadOnlyList<ComponentViewModel> children, IReadOnlyDictionary<(int, string), string> aliases)
    {
        var result = new List<WaveguideConnectionViewModel>();
        foreach (var pair in imported.Connections.Where(p => !p.A.IsTopLevelPort && !p.B.IsTopLevelPort))
        {
            var start = children[pair.A.InstanceIndex].Component.PhysicalPins.Single(p => p.Name == aliases.GetValueOrDefault((pair.A.InstanceIndex, pair.A.PinName), pair.A.PinName));
            var end = children[pair.B.InstanceIndex].Component.PhysicalPins.Single(p => p.Name == aliases.GetValueOrDefault((pair.B.InstanceIndex, pair.B.PinName), pair.B.PinName));
            var a = start.GetAbsolutePosition();
            var b = end.GetAbsolutePosition();
            var connection = new WaveguideConnection { StartPin = start, EndPin = end, IsRouteFrozen = true };
            var path = new RoutedPath();
            path.Segments.Add(new StraightSegment(a.x, a.y, b.x, b.y, start.GetAbsoluteAngle()));
            connection.RestoreCachedPath(path);
            result.Add(new WaveguideConnectionViewModel(connection));
        }
        return result;
    }
}

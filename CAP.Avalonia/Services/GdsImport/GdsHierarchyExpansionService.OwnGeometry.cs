using System.Globalization;
using System.Text;
using System.Text.Json;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

public sealed partial class GdsHierarchyExpansionService
{
    // Own polygons remain a real exact-outline obstacle and export only themselves.
    private static void AddOwnGeometry(Component parent, GdsCircuitImport imported,
        List<ComponentViewModel> children, List<PdkComponentDraft> drafts, DesignScopedGdsSet set,
        double parentWidth, double parentHeight)
    {
        var polygons = imported.TopCellResidualPolygons.Concat(imported.TopCellWaveguidePolygons).ToList();
        if (polygons.Count == 0) return;
        var points = polygons.SelectMany(p => p.Points).ToList();
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        var width = maxX - minX; var height = maxY - minY;
        var outlines = polygons.Select(p => new OutlinePolygon
        {
            Layer = p.Layer, DataType = p.DataType,
            Points = p.Points.Select(point => new OutlinePoint(point.X - minX, point.Y - minY)).ToList()
        }).ToList();
        var name = GdsCellDraftMapper.SanitizeComponentName(imported.TopCellName) + "__own_" + Fingerprint(JsonSerializer.Serialize(outlines));
        var code = new StringBuilder("import nazca as nd\ndef component():\n    with nd.Cell() as cell:\n");
        foreach (var polygon in outlines)
        {
            var coordinates = string.Join(", ", polygon.Points.Select(p =>
                $"({p.X.ToString("R", CultureInfo.InvariantCulture)}, {(height - p.Y).ToString("R", CultureInfo.InvariantCulture)})"));
            code.Append(CultureInfo.InvariantCulture, $"        nd.Polygon(points=[{coordinates}], layer=({polygon.Layer}, {polygon.DataType})).put(0, 0)\n");
        }
        code.Append("    return cell\n");
        var draft = new PdkComponentDraft
        {
            Name = name, Category = GdsCellDraftMapper.ImportCategory,
            WidthMicrometers = width, HeightMicrometers = height, OutlinePolygons = outlines,
            RawCode = code.ToString(), RawCodeBackend = "nazca", ImportedModelStatus = "geometry-only",
            ImportedSourceCellName = imported.TopCellName, ImportedOwnGeometryOnly = true
        };
        var existing = set.Drafts.FirstOrDefault(d => d.Name == name);
        if (existing is null) drafts.Add(draft);
        var template = PdkTemplateConverter.ConvertToTemplate(existing ?? draft, set.PdkName, null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var center = Transform(parent, minX + width / 2, minY + height / 2, parentWidth, parentHeight);
        if (parent.IsMirroredHorizontally) ComponentPoseTransform.MirrorPinsHorizontally(component);
        ComponentPoseTransform.ApplyExactRotation(component, parent.RotationDegrees);
        component.PhysicalX = center.X - component.WidthMicrometers / 2;
        component.PhysicalY = center.Y - component.HeightMicrometers / 2;
        component.IsRoutingObstacle = parent.IsRoutingObstacle;
        component.HumanReadableName = imported.TopCellName + " (own geometry)";
        children.Add(new ComponentViewModel(component, name, set.PdkName));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

public sealed partial class GdsHierarchyExpansionService
{
    // Existing parent interface metadata wins over newly detected child labels/kinds.
    // The exact spatial correspondence must be unique; no proximity or name guessing.
    private static Dictionary<(int, string), string> PreserveParentPorts(Component parent,
        List<ComponentViewModel> children, Dictionary<string, ComponentTemplate> templates,
        GdsCircuitImport imported, List<PdkComponentDraft> drafts, DesignScopedGdsSet set)
    {
        var aliases = new Dictionary<(int, string), string>();
        var changes = new Dictionary<int, Dictionary<string, PhysicalPin>>();
        var used = new HashSet<PhysicalPin>();
        foreach (var pin in parent.PhysicalPins)
        {
            var position = pin.GetAbsolutePosition();
            var matches = children.SelectMany((child, i) => child.Component.PhysicalPins.Select(p => (Pin: p, Index: i)))
                .Where(candidate =>
                {
                    var point = candidate.Pin.GetAbsolutePosition();
                    var angle = ((candidate.Pin.GetAbsoluteAngle() - pin.GetAbsoluteAngle()) % 360 + 540) % 360 - 180;
                    return Math.Abs(point.x - position.x) < 1e-6 && Math.Abs(point.y - position.y) < 1e-6 && Math.Abs(angle) < 1e-6;
                }).ToList();
            if (matches.Count > 1 || (matches.Count == 1 && !used.Add(matches[0].Pin)))
                throw new InvalidOperationException($"Port '{pin.Name}' has ambiguous source child ownership. Review the source ports before expanding.");
            if (matches.Count == 0 && children.Count == imported.Instances.Count)
                throw new InvalidOperationException($"Port '{pin.Name}' has no matching child and the source cell has no own geometry to retain its interface. Review the source ports before expanding.");
            var match = matches.Count == 1 ? matches[0] : (Pin: pin, Index: children.Count - 1);
            if (!changes.TryGetValue(match.Index, out var pins)) changes[match.Index] = pins = new();
            pins.Add(match.Pin.Name, pin);
            aliases[(match.Index, match.Pin.Name)] = pin.Name;
        }
        foreach (var (index, pins) in changes)
        {
            var own = index >= imported.Instances.Count;
            var source = own ? drafts.Concat(set.Drafts).First(d => d.Name == children[index].TemplateName)
                : templates[imported.Instances[index].CellName].SourceDraft!;
            var variant = JsonSerializer.Deserialize<PdkComponentDraft>(JsonSerializer.Serialize(source))!;
            if (own)
                foreach (var parentPin in pins.Values)
                {
                    var child = children[index].Component;
                    var point = parentPin.GetAbsolutePosition();
                    var x = point.x - child.PhysicalX - child.WidthMicrometers / 2;
                    var y = point.y - child.PhysicalY - child.HeightMicrometers / 2;
                    var radians = child.RotationDegrees * Math.PI / 180;
                    var localX = x * Math.Cos(radians) + y * Math.Sin(radians);
                    var localY = -x * Math.Sin(radians) + y * Math.Cos(radians);
                    if (child.IsMirroredHorizontally) localY = -localY;
                    var angle = parentPin.GetAbsoluteAngle() - child.RotationDegrees;
                    if (child.IsMirroredHorizontally) angle = -angle;
                    variant.Pins.Add(new PhysicalPinDraft { Name = parentPin.Name,
                        OffsetXMicrometers = localX + source.WidthMicrometers / 2,
                        OffsetYMicrometers = localY + source.HeightMicrometers / 2,
                        AngleDegrees = ((angle % 360) + 360) % 360 });
                }
            foreach (var pin in variant.Pins)
            {
                if (!pins.TryGetValue(pin.Name, out var parentPin)) continue;
                pin.Name = parentPin.Name;
                pin.PinKind = parentPin.MatterType == CAP_Core.Components.Core.MatterType.Electricity
                    ? PinKindHelper.ElectricalKindName : null;
                pin.WaveguideWidthMicrometers = parentPin.WaveguideWidthMicrometers;
                pin.Layer = parentPin.Layer;
                pin.PortType = parentPin.PortType;
                pin.Polarization = parentPin.LogicalPin?.Polarization.ToString();
            }
            if (variant.Pins.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != variant.Pins.Count)
                throw new InvalidOperationException("Preserving the parent ports would give a child duplicate port names. Review the source ports before expanding.");
            variant.ImportedSourceCellName = own ? imported.TopCellName : imported.Instances[index].CellName;
            variant.ImportedModelStatus = "geometry-only";
            variant.SMatrix = null;
            variant.Name = source.Name + "__ports_" + Fingerprint(JsonSerializer.Serialize(variant.Pins));
            var existing = set.Drafts.FirstOrDefault(d => d.Name == variant.Name) ?? drafts.FirstOrDefault(d => d.Name == variant.Name);
            if (existing is null) drafts.Add(variant);
            var old = children[index];
            var template = PdkTemplateConverter.ConvertToTemplate(existing ?? variant, set.PdkName, null);
            var component = ComponentTemplates.CreateFromTemplate(template, old.Component.PhysicalX, old.Component.PhysicalY);
            if (old.Component.IsMirroredHorizontally) ComponentPoseTransform.MirrorPinsHorizontally(component);
            ComponentPoseTransform.ApplyExactRotation(component, old.Component.RotationDegrees);
            component.IsRoutingObstacle = parent.IsRoutingObstacle;
            children[index] = new ComponentViewModel(component, template.Name, set.PdkName);
        }
        return aliases;
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}

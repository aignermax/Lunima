using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;

/// <summary>
/// Maps Nazca preview pins to component-local <see cref="OverridePinData"/> and applies
/// persisted pin overrides to a live <see cref="Component"/> (issue #561). Shared by the
/// per-instance Nazca code editor (Apply/Reset) and the project-load path.
/// </summary>
public static class OverridePinMapper
{
    /// <summary>
    /// Converts the preview's pin stubs to component-local <see cref="OverridePinData"/>
    /// using a bounding-box–relative coordinate transform:
    /// <list type="bullet">
    /// <item><c>OffsetX = previewPin.X − bbox.XMin</c></item>
    /// <item><c>OffsetY = bbox.YMax − previewPin.Y</c> (Y-axis flip to Y-down app space)</item>
    /// <item><c>AngleDegrees = −previewPin.Angle</c> (Y-axis flip)</item>
    /// </list>
    /// </summary>
    public static List<OverridePinData> BuildOverridePins(NazcaPreviewResult preview)
    {
        return preview.Pins.Select(p => new OverridePinData
        {
            Name = p.Name,
            OffsetXMicrometers = p.X - preview.XMin,
            OffsetYMicrometers = preview.YMax - p.Y,
            AngleDegrees = -p.Angle,
        }).ToList();
    }

    /// <summary>
    /// Returns true when both lists have the same set of pin names (order-independent).
    /// An empty or null list is considered "matching" to avoid false positives when
    /// no pins are defined.
    /// </summary>
    public static bool PinNamesMatch(
        IReadOnlyList<OverridePinData>? a, IReadOnlyList<OverridePinData>? b)
    {
        if ((a == null || a.Count == 0) && (b == null || b.Count == 0))
            return true;
        if (a == null || b == null)
            return false;
        if (a.Count != b.Count)
            return false;
        var namesA = a.Select(p => p.Name).OrderBy(n => n).ToList();
        var namesB = b.Select(p => p.Name).OrderBy(n => n).ToList();
        return namesA.SequenceEqual(namesB);
    }

    /// <summary>
    /// Snapshots the component's current physical pins as <see cref="OverridePinData"/> DTOs.
    /// </summary>
    public static List<OverridePinData> CaptureAsPinData(IEnumerable<PhysicalPin> pins)
        => pins.Select(p => new OverridePinData
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
        }).ToList();

    /// <summary>
    /// Replaces the component's physical pin list with pins derived from
    /// <paramref name="pinData"/>. <c>LogicalPin</c> links are not restored
    /// (override pins have no S-matrix tie-in).
    /// </summary>
    public static void ApplyPinsToComponent(Component comp, IReadOnlyList<OverridePinData> pinData)
    {
        comp.PhysicalPins.Clear();
        foreach (var pd in pinData)
        {
            comp.PhysicalPins.Add(new PhysicalPin
            {
                Name = pd.Name,
                OffsetXMicrometers = pd.OffsetXMicrometers,
                OffsetYMicrometers = pd.OffsetYMicrometers,
                AngleDegrees = pd.AngleDegrees,
                ParentComponent = comp,
            });
        }
    }
}

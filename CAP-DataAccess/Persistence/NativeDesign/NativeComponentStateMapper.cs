using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>Maps persisted component pose and slider state.</summary>
public static class NativeComponentStateMapper
{
    /// <summary>
    /// Captures every slider value keyed by slider number, so multi-parameter
    /// components round-trip all values. Null when the component has no sliders.
    /// </summary>
    public static Dictionary<int, double>? SnapshotSliderValues(CAP_Core.Components.Core.Component component)
    {
        var sliders = component.GetAllSliders();
        if (sliders.Count == 0) return null;
        return sliders.ToDictionary(s => s.Number, s => s.Value);
    }

    /// <summary>
    /// Restores saved slider values onto a component. Prefers the multi-slider map
    ///; falls back to the legacy single value (slider 0) for old files.
    /// </summary>
    public static void RestoreSliderValues(
        CAP_Core.Components.Core.Component component,
        Dictionary<int, double>? sliderValues,
        double? legacySliderValue)
    {
        if (sliderValues != null)
        {
            foreach (var (number, value) in sliderValues)
            {
                var slider = component.GetSlider(number);
                if (slider != null) slider.Value = value;
            }
            return;
        }

        if (legacySliderValue.HasValue)
        {
            var slider = component.GetSlider(0);
            if (slider != null) slider.Value = legacySliderValue.Value;
        }
    }

    /// <summary>
    /// Restores a freshly template-created component's saved pose in placement
    /// order: mirror first (local, unrotated frame — matching
    /// placement), then the
    /// discrete quarter turns, then the exact continuous angle when the file
    /// carries one (GDS-imported non-cardinal instance; null in older files
    /// and for cardinal rotations).
    /// </summary>
    public static void RestorePose(
        Component component, bool? mirrored, int quarterTurns, double? exactRotationDegrees)
    {
        if (mirrored == true)
            ComponentPoseTransform.MirrorPinsHorizontally(component);

        for (int i = 0; i < quarterTurns; i++)
            ComponentPoseTransform.Rotate90CounterClockwise(component);

        if (exactRotationDegrees is double exact)
            ComponentPoseTransform.ApplyExactRotation(component, exact);
    }
}

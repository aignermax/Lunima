using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
    // Each domain is matched as one planar geometry set. This is a review
    // warning, never a restriction on legitimate multilayer process designs.
    public bool HasCombinedRouteLayers =>
        Layers.Count(x => (x.Role & GdsLayerRole.OpticalCore) != 0) > 1 ||
        Layers.Count(x => (x.Role & (GdsLayerRole.Electrical | GdsLayerRole.Heater)) != 0) > 1;

    public string CombinedRouteLayerWarning =>
        HasCombinedRouteLayers ? Text("CombinedRouteLayerWarning") : "";
}

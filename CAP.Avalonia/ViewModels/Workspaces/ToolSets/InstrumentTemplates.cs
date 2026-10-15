using CAP.Avalonia.Controls.Rendering.DeviceCategories;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.ViewModels.Workspaces.ToolSets;

/// <summary>
/// Which library templates are instruments — the parts the Optics workspace places: light
/// sources and fiber / grating / edge couplers (light enters or leaves), detectors and
/// terminators (light is absorbed or measured) and the analysis tools (ONA analyzer, probes).
/// </summary>
public static class InstrumentTemplates
{
    /// <summary>Library category of the process-agnostic analysis tools PDK.</summary>
    public const string AnalysisCategory = "Analysis";

    /// <summary>True when <paramref name="template"/> is an instrument.</summary>
    public static bool IsInstrument(ComponentTemplate template)
    {
        if (string.Equals(template.Category, AnalysisCategory, StringComparison.OrdinalIgnoreCase))
            return true;
        var category = DeviceCategoryClassifier.ClassifyNames(
            template.Name, null, template.NazcaFunctionName, hasElectricalPin: false);
        return category is DeviceCategory.InputOutput or DeviceCategory.Detector;
    }
}

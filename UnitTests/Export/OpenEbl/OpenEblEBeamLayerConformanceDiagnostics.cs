using System.Text;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1447 diagnostics: when the EBeam export profile unexpectedly resolves to
/// null, the bare "script contains 1003" failure says nothing about WHICH shared
/// state leaked in. This dump lists every component and optical pin the profile
/// resolution saw — module names and width/layer stamps — so the next occurrence
/// identifies the polluted input directly from the test log.
/// </summary>
internal static class OpenEblEBeamLayerConformanceDiagnostics
{
    /// <summary>Describes every canvas component and optical pin stamp the profile reads.</summary>
    public static string Describe(DesignCanvasViewModel canvas)
    {
        var sb = new StringBuilder("EBeam export profile resolved to null. Canvas state:\n");
        foreach (var vm in canvas.Components)
        {
            var c = vm.Component;
            sb.AppendLine(
                $"  component '{c.Identifier}' module='{c.NazcaModuleName ?? "<null>"}' " +
                $"func='{c.NazcaFunctionName ?? "<null>"}' analysis={c.IsAnalysisTool} group={c is ComponentGroup}");
            foreach (var pin in c.PhysicalPins)
            {
                sb.AppendLine(
                    $"    pin '{pin.Name}' matter={pin.MatterType} " +
                    $"width={pin.WaveguideWidthMicrometers?.ToString() ?? "<null>"} " +
                    $"layer={pin.Layer?.ToString() ?? "<null>"}");
            }
        }
        return sb.ToString();
    }
}

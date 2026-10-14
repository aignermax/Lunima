using System.Globalization;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// A text key that is equal for two gate groups whose truth tables must be equal:
/// same pin roles and threshold, same wavelength, and the same children (template,
/// position relative to the group, rotation, slider settings) joined by internal
/// paths of the same length. A large design repeats a handful of gate cells hundreds
/// of times, so classifying one representative per key keeps the gate view cheap.
/// </summary>
public static class GateStructureSignature
{
    private const string RealFormat = "0.###";

    /// <summary>Builds the signature of a gate group at a wavelength.</summary>
    /// <param name="group">A group carrying a persisted pin assignment.</param>
    /// <param name="wavelengthNm">The wavelength the gate is classified at.</param>
    public static string Of(ComponentGroup group, int wavelengthNm)
    {
        ArgumentNullException.ThrowIfNull(group);
        var assignment = group.TruthTablePinAssignment
            ?? throw new ArgumentException($"Group '{group.GroupName}' is no gate.", nameof(group));
        var key = new StringBuilder();
        key.Append(wavelengthNm).Append('|')
            .Append(string.Join(",", assignment.InputPinNames)).Append('>')
            .Append(string.Join(",", assignment.OutputPinNames)).Append('+')
            .Append(string.Join(",", assignment.BiasPinNames)).Append('@')
            .Append(Real(assignment.Threshold)).Append(assignment.IsRegister ? "R" : "");
        AppendChildren(key, group, group.PhysicalX, group.PhysicalY);
        return key.ToString();
    }

    private static void AppendChildren(StringBuilder key, ComponentGroup group, double originX, double originY)
    {
        var children = group.ChildComponents
            .OrderBy(child => child.PhysicalX - originX)
            .ThenBy(child => child.PhysicalY - originY);
        foreach (var child in children)
        {
            key.Append('[').Append(child.TemplateName ?? child.NazcaFunctionName)
                .Append(':').Append(child.NazcaFunctionParameters)
                .Append(':').Append(Real(child.PhysicalX - originX))
                .Append(',').Append(Real(child.PhysicalY - originY))
                .Append(',').Append(Real(child.RotationDegrees))
                .Append(child.IsMirroredHorizontally ? "m" : "");
            foreach (var slider in child.GetAllSliders().OrderBy(s => s.Number))
                key.Append(";s").Append(slider.Number).Append('=').Append(Real(slider.Value));
            if (child is ComponentGroup nested)
                AppendChildren(key, nested, originX, originY);
            key.Append(']');
        }
        foreach (var path in group.InternalPaths)
        {
            key.Append("{p").Append(path.StartPin?.Name).Append('-').Append(path.EndPin?.Name)
                .Append('=').Append(Real(path.Path?.TotalLengthMicrometers ?? 0)).Append('}');
        }
    }

    private static string Real(double value) => value.ToString(RealFormat, CultureInfo.InvariantCulture);
}

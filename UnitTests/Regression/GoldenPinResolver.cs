using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;

namespace UnitTests.Regression;

/// <summary>Pin reference resolved to a physical pin (e.g. "mzi_splitter.in").</summary>
internal sealed record ResolvedPin(string Reference, PhysicalPin Pin);

/// <summary>Resolved pin plus its declared input power.</summary>
internal sealed record ResolvedInput(string Reference, PhysicalPin Pin, double Power);

/// <summary>
/// Resolves manifest pin references ("component.pin") to physical pins and
/// attaches declared inputs to a port manager. Unresolvable references are
/// recorded as gate violations on the result.
/// </summary>
internal static class GoldenPinResolver
{
    /// <summary>Attaches each resolved input as a light source on the port manager.</summary>
    public static void AttachInputs(
        PhysicalExternalPortManager portManager, List<ResolvedInput> inputs)
    {
        foreach (var input in inputs)
        {
            var external = new ExternalInput(
                $"golden_{input.Reference}", LaserType.Red, 0, new Complex(input.Power, 0));
            portManager.AddLightSource(external, input.Pin.LogicalPin!.IDInFlow);
        }
    }

    /// <summary>Resolves declared inputs; unresolvable references add gate violations.</summary>
    public static List<ResolvedInput> ResolveInputs(
        IEnumerable<GoldenInput> inputs, List<Component> allComponents, GoldenGateResult result)
    {
        var resolved = new List<ResolvedInput>();
        foreach (var input in inputs)
        {
            var pin = ResolvePin(input.Pin, allComponents, result);
            if (pin != null) resolved.Add(new ResolvedInput(input.Pin, pin, input.Power));
        }
        return resolved;
    }

    /// <summary>Resolves declared outputs; unresolvable references add gate violations.</summary>
    public static List<ResolvedPin> ResolveOutputs(
        IEnumerable<string> outputs, List<Component> allComponents, GoldenGateResult result)
    {
        var resolved = new List<ResolvedPin>();
        foreach (var output in outputs)
        {
            var pin = ResolvePin(output, allComponents, result);
            if (pin != null) resolved.Add(new ResolvedPin(output, pin));
        }
        return resolved;
    }

    /// <summary>Formats a pin as "component.pin" for diagnostics.</summary>
    public static string PinName(PhysicalPin pin) =>
        $"{pin.ParentComponent.Identifier}.{pin.Name}";

    private static PhysicalPin? ResolvePin(
        string pinReference, List<Component> allComponents, GoldenGateResult result)
    {
        int separator = pinReference.IndexOf('.');
        var component = separator > 0
            ? allComponents.FirstOrDefault(c =>
                string.Equals(c.Identifier, pinReference[..separator], StringComparison.Ordinal))
            : null;
        var pin = component?.PhysicalPins.FirstOrDefault(p =>
            string.Equals(p.Name, pinReference[(separator + 1)..], StringComparison.Ordinal));
        if (pin?.LogicalPin == null)
            result.Violations.Add(
                $"pin reference '{pinReference}' does not resolve to a component pin");
        return pin;
    }
}

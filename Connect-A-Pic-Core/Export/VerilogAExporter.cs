using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Exports photonic circuits to Verilog-A format for SPICE-based co-simulation.
/// Generates component library files (<c>.va</c>), a top-level netlist, and an
/// optional SPICE test bench. Errors are loud: unmapped pins, empty components,
/// or components without physical ports throw <see cref="InvalidOperationException"/>
/// instead of silently emitting a broken file.
/// </summary>
public class VerilogAExporter
{
    /// <summary>
    /// Exports a photonic circuit to Verilog-A files.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a component has no physical pins, when a connection references
    /// a pin that doesn't belong to any exported component, or when the design is
    /// empty. Use <see cref="VerilogAExportResult.Failure"/> only for expected
    /// user-facing failures surfaced via the result pattern.
    /// </exception>
    public VerilogAExportResult Export(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        VerilogAExportOptions options)
    {
        if (components.Count == 0)
            return VerilogAExportResult.Failure("No components in design.");

        try
        {
            ValidateComponents(components);
            var componentFiles = BuildComponentLibrary(components, options.WavelengthNm);
            var (netlist, portNodes) = VerilogANetlistWriter.WriteTopLevel(
                components, connections, options.CircuitName);
            var testBench = options.IncludeTestBench
                ? VerilogANetlistWriter.WriteSpiceTestBench(options.CircuitName, components, connections)
                : string.Empty;

            return new VerilogAExportResult
            {
                Success = true,
                ComponentFiles = componentFiles,
                TopLevelNetlist = netlist,
                SpiceTestBench = testBench,
                CircuitName = options.CircuitName
            };
        }
        catch (InvalidOperationException ex)
        {
            return VerilogAExportResult.Failure(ex.Message);
        }
    }

    private static void ValidateComponents(IReadOnlyList<Component> components)
    {
        foreach (var comp in components)
        {
            if (comp.PhysicalPins == null || comp.PhysicalPins.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Component '{comp.Name ?? comp.Identifier}' has no physical ports. " +
                    "Verilog-A requires every component to declare at least one optical port.");
            }
        }
    }

    private static Dictionary<string, string> BuildComponentLibrary(
        IReadOnlyList<Component> components, int wavelengthNm)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var moduleToSource = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var comp in components)
        {
            var moduleName = VerilogAIdentifier.For(comp);
            var sourceKey = comp.NazcaFunctionName ?? comp.Name ?? "";

            if (moduleToSource.TryGetValue(moduleName, out var existingSource))
            {
                // Same underlying source (same NazcaFunctionName) → legitimate dedup.
                // Different source → sanitization collision that would silently share a
                // module file between two physically different components.
                if (!string.Equals(existingSource, sourceKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Verilog-A module name collision: '{existingSource}' and '{sourceKey}' " +
                        $"both sanitize to '{moduleName}'. Rename one component's NazcaFunctionName " +
                        "to avoid two different component models sharing a single .va file.");
                }
                continue;
            }

            moduleToSource[moduleName] = sourceKey;
            files[$"{moduleName}.va"] = VerilogAModuleWriter.Write(comp, moduleName, wavelengthNm);
        }

        return files;
    }
}

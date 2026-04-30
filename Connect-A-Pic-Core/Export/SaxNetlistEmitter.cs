using System.Globalization;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Emits the sax netlist (instances + connections + ports), builds the
/// circuit function, runs a wavelength sweep, and plots the transmission
/// between an automatically-picked input and output port.
///
/// <para>
/// Ports are derived from <b>dangling pins</b> — any PhysicalPin that does
/// not appear as an endpoint of a resolved connection becomes a circuit-
/// level port. If the design has fewer than two such pins, the sweep and
/// plot sections still run but print a clear TODO asking the user to choose
/// input/output explicitly.
/// </para>
/// </summary>
internal static class SaxNetlistEmitter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal static void AppendNetlistAndSweep(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        IReadOnlyList<ResolvedConnection> connections,
        double wlMinNm,
        double wlMaxNm,
        int numPoints)
    {
        var ports = CollectDanglingPins(components, connections);
        var (inputPort, outputPort) = PickSweepPorts(ports, connections);

        AppendNetlist(sb, components, connections, ports);
        AppendCircuitBuild(sb, components);
        AppendSweepAndPlot(sb, ports, inputPort, outputPort, wlMinNm, wlMaxNm, numPoints);
    }

    private static void AppendNetlist(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        IReadOnlyList<ResolvedConnection> connections,
        IReadOnlyList<PortEntry> ports)
    {
        sb.AppendLine("# ── Circuit netlist ─────────────────────────────────────────────────");
        sb.AppendLine("netlist = {");

        sb.AppendLine("    'instances': {");
        foreach (var comp in components)
        {
            var varName = SaxIdentifier.ForVar(comp);
            sb.AppendLine($"        '{varName}': '{varName}_model',");
        }
        sb.AppendLine("    },");

        sb.AppendLine("    'connections': {");
        foreach (var conn in connections)
        {
            var aVar = SaxIdentifier.ForVar(conn.Start.ParentComponent);
            var aPin = SaxIdentifier.ForPin(conn.Start.Name);
            var bVar = SaxIdentifier.ForVar(conn.End.ParentComponent);
            var bPin = SaxIdentifier.ForPin(conn.End.Name);
            sb.AppendLine($"        '{aVar},{aPin}': '{bVar},{bPin}',");
        }
        sb.AppendLine("    },");

        sb.AppendLine("    'ports': {");
        if (ports.Count == 0)
        {
            sb.AppendLine("        # No dangling pins found — the design is fully connected.");
            sb.AppendLine("        # sax requires at least one external port to simulate; add a");
            sb.AppendLine("        # port manually here, or remove an interior connection so the");
            sb.AppendLine("        # circuit has external inputs/outputs.");
        }
        else
        {
            foreach (var p in ports)
                sb.AppendLine($"        '{p.PortName}': '{p.CompVar},{p.PinName}',");
        }
        sb.AppendLine("    },");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendCircuitBuild(StringBuilder sb, IReadOnlyList<Component> components)
    {
        sb.AppendLine("circuit_fn, _circuit_info = sax.circuit(");
        sb.AppendLine("    netlist=netlist,");
        sb.AppendLine("    models={");
        var emittedModels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var comp in components)
        {
            var varName = SaxIdentifier.ForVar(comp);
            if (!emittedModels.Add(varName)) continue;
            sb.AppendLine($"        '{varName}_model': {varName}_model,");
        }
        sb.AppendLine("    },");
        sb.AppendLine(")");
        sb.AppendLine();
    }

    private static void AppendSweepAndPlot(
        StringBuilder sb,
        IReadOnlyList<PortEntry> ports,
        string? inputPort,
        string? outputPort,
        double wlMinNm, double wlMaxNm, int numPoints)
    {
        var wlMinUm = wlMinNm / 1000.0;
        var wlMaxUm = wlMaxNm / 1000.0;

        sb.AppendLine("# ── Wavelength sweep (wavelengths in micrometres — sax convention) ──");
        sb.AppendLine(
            $"wavelengths_um = np.linspace({wlMinUm.ToString(Inv)}, " +
            $"{wlMaxUm.ToString(Inv)}, {numPoints})");
        sb.AppendLine();

        if (ports.Count == 0 || inputPort == null || outputPort == null)
        {
            sb.AppendLine("# No external ports — nothing to plot. Add a port to the netlist above");
            sb.AppendLine("# and re-run. Skipping sweep.");
            sb.AppendLine("INPUT_PORT = None");
            sb.AppendLine("OUTPUT_PORT = None");
            sb.AppendLine("transmission = []");
        }
        else
        {
            sb.AppendLine("# Circuit-level ports detected in the export:");
            foreach (var p in ports)
                sb.AppendLine($"#   - {p.PortName}   (= {p.CompVar},{p.PinName})");
            sb.AppendLine();
            sb.AppendLine("# Defaults below were picked by flow analysis of the Lunima connection");
            sb.AppendLine("# directions (light flows Start→End). Change either line to any port name");
            sb.AppendLine("# in the list above if you want a different S-parameter.");
            sb.AppendLine($"INPUT_PORT  = '{inputPort}'");
            sb.AppendLine($"OUTPUT_PORT = '{outputPort}'");
            sb.AppendLine();
            sb.AppendLine("transmission = []");
            sb.AppendLine("for wl_um in wavelengths_um:");
            sb.AppendLine("    s = circuit_fn(wl=float(wl_um))");
            sb.AppendLine("    key = (INPUT_PORT, OUTPUT_PORT)");
            sb.AppendLine("    if key in s:");
            sb.AppendLine("        transmission.append(abs(complex(s[key])) ** 2)");
            sb.AppendLine("    elif (OUTPUT_PORT, INPUT_PORT) in s:");
            sb.AppendLine("        transmission.append(abs(complex(s[(OUTPUT_PORT, INPUT_PORT)])) ** 2)");
            sb.AppendLine("    else:");
            sb.AppendLine("        transmission.append(0.0)");
        }
        sb.AppendLine();
        AppendPlot(sb);
    }

    private static void AppendPlot(StringBuilder sb)
    {
        sb.AppendLine("# ── Plot ────────────────────────────────────────────────────────────");
        sb.AppendLine("import os");
        sb.AppendLine("plt.figure(figsize=(10, 6))");
        sb.AppendLine("if transmission:");
        sb.AppendLine("    t_db = 10 * np.log10(np.maximum(transmission, 1e-15))");
        sb.AppendLine("    plt.plot(wavelengths_um * 1000.0, t_db)");
        sb.AppendLine("    plt.xlabel('Wavelength (nm)')");
        sb.AppendLine("    plt.ylabel(f'|S({INPUT_PORT}->{OUTPUT_PORT})|^2 (dB)')");
        sb.AppendLine("else:");
        sb.AppendLine("    plt.text(0.5, 0.5, 'No transmission to plot', ha='center', va='center',");
        sb.AppendLine("             transform=plt.gca().transAxes)");
        sb.AppendLine("plt.title('Lunima circuit transmission spectrum')");
        sb.AppendLine("plt.grid(True)");
        sb.AppendLine("plt.tight_layout()");
        sb.AppendLine();
        sb.AppendLine("# Always save a PNG next to this script so the result is available even");
        sb.AppendLine("# when no interactive matplotlib backend is installed (the venv shipped");
        sb.AppendLine("# to CI does not have a GUI toolkit).");
        sb.AppendLine("try:");
        sb.AppendLine("    _png_path = os.path.splitext(os.path.abspath(__file__))[0] + '.png'");
        sb.AppendLine("    plt.savefig(_png_path, dpi=150)");
        sb.AppendLine("    print(f'[Lunima] Spectrum saved to: {_png_path}')");
        sb.AppendLine("except NameError:");
        sb.AppendLine("    # __file__ not defined (e.g. running via `python -c`); skip the save.");
        sb.AppendLine("    pass");
        sb.AppendLine();
        sb.AppendLine("try:");
        sb.AppendLine("    plt.show()");
        sb.AppendLine("except Exception as _e:");
        sb.AppendLine("    # Non-interactive backend — the PNG above is the result.");
        sb.AppendLine("    print(f'[Lunima] plt.show() skipped: {_e}')");
    }

    /// <summary>
    /// Picks default <c>INPUT_PORT</c> / <c>OUTPUT_PORT</c> values for the
    /// sweep by analysing the direction of each connection. Light flows
    /// Start→End in a <see cref="ResolvedConnection"/>, so:
    /// <list type="bullet">
    ///   <item><description>A component whose connected pins are all
    ///   <c>Start</c>s is a <b>source</b>; its dangling pins are input
    ///   candidates.</description></item>
    ///   <item><description>A component whose connected pins are all
    ///   <c>End</c>s is a <b>sink</b>; its dangling pins are output
    ///   candidates.</description></item>
    /// </list>
    /// Components that appear on both sides (pass-throughs) are skipped for
    /// this heuristic. If no clear source or sink exists, falls back to the
    /// first two dangling pins in the list.
    /// </summary>
    private static (string? Input, string? Output) PickSweepPorts(
        IReadOnlyList<PortEntry> ports,
        IReadOnlyList<ResolvedConnection> connections)
    {
        if (ports.Count == 0) return (null, null);
        if (ports.Count == 1) return (ports[0].PortName, ports[0].PortName);

        // Tally each component's role: did its connected pins appear as the
        // Start of connections, the End, or both?
        var asStart = new HashSet<Component>();
        var asEnd = new HashSet<Component>();
        foreach (var c in connections)
        {
            asStart.Add(c.Start.ParentComponent);
            asEnd.Add(c.End.ParentComponent);
        }

        string? input = ports
            .FirstOrDefault(p => IsSourceComponent(p, asStart, asEnd))
            .PortName;
        string? output = ports
            .FirstOrDefault(p => IsSinkComponent(p, asStart, asEnd) && p.PortName != input)
            .PortName;

        // Fallback to the first two dangling pins when the heuristic couldn't
        // classify — better a defensible default than null.
        input  ??= ports[0].PortName;
        output ??= ports.FirstOrDefault(p => p.PortName != input).PortName ?? ports[0].PortName;

        return (input, output);
    }

    private static bool IsSourceComponent(PortEntry p, HashSet<Component> asStart, HashSet<Component> asEnd)
    {
        var comp = FindComponentByVar(p.CompVar, asStart.Concat(asEnd));
        return comp != null && asStart.Contains(comp) && !asEnd.Contains(comp);
    }

    private static bool IsSinkComponent(PortEntry p, HashSet<Component> asStart, HashSet<Component> asEnd)
    {
        var comp = FindComponentByVar(p.CompVar, asStart.Concat(asEnd));
        return comp != null && asEnd.Contains(comp) && !asStart.Contains(comp);
    }

    private static Component? FindComponentByVar(string varName, IEnumerable<Component> pool)
    {
        foreach (var c in pool)
            if (SaxIdentifier.ForVar(c) == varName)
                return c;
        return null;
    }

    /// <summary>
    /// A pin that was never connected to anything → an external port on the
    /// generated sax circuit.
    /// </summary>
    private readonly record struct PortEntry(string PortName, string CompVar, string PinName);

    private static List<PortEntry> CollectDanglingPins(
        IReadOnlyList<Component> components,
        IReadOnlyList<ResolvedConnection> connections)
    {
        // Use (Component, pin-name) as identity since PhysicalPin reference
        // equality is reliable for this traversal but the pair is easier to
        // reason about when debugging and matches what we'll emit.
        var usedPins = new HashSet<(Component Comp, string PinName)>();
        foreach (var c in connections)
        {
            usedPins.Add((c.Start.ParentComponent, c.Start.Name));
            usedPins.Add((c.End.ParentComponent, c.End.Name));
        }

        var ports = new List<PortEntry>();
        var portNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comp in components)
        {
            var varName = SaxIdentifier.ForVar(comp);
            foreach (var pin in comp.PhysicalPins)
            {
                if (usedPins.Contains((comp, pin.Name))) continue;

                var pinId = SaxIdentifier.ForPin(pin.Name);
                var portName = $"{varName}_{pinId}";
                // Dedupe in the unlikely case two dangling pins collide after
                // identifier sanitisation — suffix with an index.
                int i = 1;
                var finalName = portName;
                while (!portNames.Add(finalName))
                    finalName = $"{portName}_{++i}";

                ports.Add(new PortEntry(finalName, varName, pinId));
            }
        }

        return ports;
    }
}

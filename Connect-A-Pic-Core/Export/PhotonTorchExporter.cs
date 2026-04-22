using System.Globalization;
using System.Text;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Exports Lunima photonic designs to PhotonTorch Python scripts.
/// Generates a runnable script using the context-manager Network API:
/// <c>with pt.Network() as nw: ...</c>. Ports are addressed by zero-based
/// index (their position in the component's PhysicalPins list). Every
/// unconnected port is auto-terminated with <c>pt.Detector()</c>; the first
/// unconnected port becomes <c>pt.Source()</c> so the script has a signal
/// to inject.
/// </summary>
public class PhotonTorchExporter
{
    private const double DefaultCouplingRatio = 0.5;
    private const double SpeedOfLight = 299792458.0;

    /// <summary>Options controlling the generated PhotonTorch simulation script.</summary>
    public class ExportOptions
    {
        /// <summary>Wavelength in nanometers for simulation.</summary>
        public double WavelengthNm { get; init; } = 1550.0;

        /// <summary>Simulation mode: steady-state or time-domain.</summary>
        public SimulationMode Mode { get; init; } = SimulationMode.SteadyState;

        /// <summary>Bit rate in bits/second for time-domain simulation.</summary>
        public double BitRateGbps { get; init; } = 1.0;

        /// <summary>Number of time steps for time-domain simulation.</summary>
        public int TimeDomainSteps { get; init; } = 1000;
    }

    /// <summary>Simulation mode for the generated PhotonTorch script.</summary>
    public enum SimulationMode
    {
        /// <summary>Continuous-wave, frequency-domain steady-state.</summary>
        SteadyState,

        /// <summary>Time-domain pulsed simulation.</summary>
        TimeDomain
    }

    /// <summary>
    /// Exports the design to a PhotonTorch Python script.
    /// </summary>
    public string Export(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        var nameMap = BuildComponentNameMap(components);
        var pinIndexMap = BuildPinIndexMap(components);
        var connectedPins = CollectConnectedPins(connections);
        var (sourceCount, detectorTerminations) = BuildTerminations(components, connectedPins, nameMap, pinIndexMap);

        AppendHeader(sb, options, ci);
        AppendNetworkBlock(sb, components, connections, nameMap, pinIndexMap, detectorTerminations, ci);
        AppendSimulation(sb, options, sourceCount, detectorTerminations.Count, ci);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ExportOptions options, CultureInfo ci)
    {
        sb.AppendLine("# PhotonTorch export from Lunima");
        sb.AppendLine("# Install: pip install 'torch<1.9' photontorch matplotlib");
        sb.AppendLine("# NOTE: photontorch 0.4.1 uses torch.solve, removed in torch >=1.9.");
        sb.AppendLine("#       Install torch<1.9 or a patched photontorch to run the simulation.");
        sb.AppendLine("import torch");
        sb.AppendLine("import photontorch as pt");
        sb.AppendLine("import matplotlib.pyplot as plt");
        sb.AppendLine();

        double freqHz = SpeedOfLight / (options.WavelengthNm * 1e-9);
        sb.AppendLine("# ── Wavelength settings ───────────────────────────────────────────");
        sb.AppendLine($"WAVELENGTH_NM = {options.WavelengthNm.ToString("F1", ci)}");
        sb.AppendLine($"FREQ_HZ = {freqHz.ToString("G6", ci)}  # c / {options.WavelengthNm.ToString("F0", ci)}nm");
        sb.AppendLine();
    }

    /// <summary>
    /// Builds a mapping from Component.Identifier to a safe Python variable name.
    /// </summary>
    private static Dictionary<string, string> BuildComponentNameMap(IReadOnlyList<Component> components)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comp in components)
        {
            var prefix = GetComponentPrefix(comp.NazcaFunctionName);
            var candidate = $"{prefix}_{Sanitize(comp.Identifier)}";
            map[comp.Identifier] = EnsureUnique(candidate, usedNames);
        }

        return map;
    }

    private static string GetComponentPrefix(string? nazcaFunctionName)
    {
        var name = (nazcaFunctionName ?? "").ToLowerInvariant();
        if (name.Contains("wg") || name.Contains("straight") || name.Contains("waveguide"))
            return "wg";
        if (name.Contains("dc") || name.Contains("directional"))
            return "dc";
        if (name.Contains("mmi") || name.Contains("splitter"))
            return "mmi";
        if (name.Contains("gc") || name.Contains("grating"))
            return "gc";
        if (name.Contains("phase") || name.Contains("ps"))
            return "ps";
        return "comp";
    }

    private static string Sanitize(string identifier)
    {
        var sb = new StringBuilder();
        foreach (var c in identifier)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 0 ? sb.ToString() : "x";
    }

    private static string EnsureUnique(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate))
            return candidate;
        for (int i = 2; ; i++)
        {
            var numbered = $"{candidate}_{i}";
            if (used.Add(numbered))
                return numbered;
        }
    }

    /// <summary>
    /// Maps each PhysicalPin.PinId to its zero-based index within its parent component.
    /// PhotonTorch addresses ports by numeric index, not by name.
    /// </summary>
    private static Dictionary<Guid, int> BuildPinIndexMap(IReadOnlyList<Component> components)
    {
        var map = new Dictionary<Guid, int>();
        foreach (var comp in components)
        {
            if (comp.PhysicalPins == null) continue;
            for (int i = 0; i < comp.PhysicalPins.Count; i++)
                map[comp.PhysicalPins[i].PinId] = i;
        }
        return map;
    }

    private static HashSet<Guid> CollectConnectedPins(IReadOnlyList<WaveguideConnection> connections)
    {
        var set = new HashSet<Guid>();
        foreach (var conn in connections)
        {
            set.Add(conn.StartPin.PinId);
            set.Add(conn.EndPin.PinId);
        }
        return set;
    }

    /// <summary>
    /// For every unconnected pin, allocate a terminal variable name.
    /// The first unconnected pin becomes the Source; the rest become Detectors.
    /// </summary>
    private readonly record struct Termination(string VarName, bool IsSource, string ComponentVar, int PortIndex);

    private static (int sourceCount, List<Termination> terminations) BuildTerminations(
        IReadOnlyList<Component> components,
        HashSet<Guid> connectedPins,
        Dictionary<string, string> nameMap,
        Dictionary<Guid, int> pinIndexMap)
    {
        var terminations = new List<Termination>();
        int detectorIndex = 0;
        bool sourceAssigned = false;

        foreach (var comp in components)
        {
            if (comp.PhysicalPins == null) continue;
            foreach (var pin in comp.PhysicalPins)
            {
                if (connectedPins.Contains(pin.PinId)) continue;
                if (!nameMap.TryGetValue(comp.Identifier, out var compVar)) continue;
                if (!pinIndexMap.TryGetValue(pin.PinId, out var portIndex)) continue;

                if (!sourceAssigned)
                {
                    terminations.Add(new Termination("src", IsSource: true, compVar, portIndex));
                    sourceAssigned = true;
                }
                else
                {
                    terminations.Add(new Termination($"det_{detectorIndex++}", IsSource: false, compVar, portIndex));
                }
            }
        }

        return (sourceAssigned ? 1 : 0, terminations);
    }

    private static void AppendNetworkBlock(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        Dictionary<string, string> nameMap,
        Dictionary<Guid, int> pinIndexMap,
        List<Termination> terminations,
        CultureInfo ci)
    {
        sb.AppendLine("# ── Network assembly ──────────────────────────────────────────────");
        sb.AppendLine("with pt.Network() as nw:");

        // Components
        foreach (var comp in components)
        {
            var varName = nameMap[comp.Identifier];
            var definition = BuildComponentDefinition(comp, ci);
            sb.AppendLine($"    nw.{varName} = {definition}");
        }

        // Terminals
        foreach (var term in terminations)
            sb.AppendLine($"    nw.{term.VarName} = pt.{(term.IsSource ? "Source" : "Detector")}()");

        sb.AppendLine();

        // Inter-component connections
        var pinToComp = BuildPinToComponentMap(components);
        foreach (var conn in connections)
        {
            var line = BuildConnectionLink(conn, nameMap, pinToComp, pinIndexMap);
            if (line != null)
                sb.AppendLine($"    {line}");
        }

        // Terminal connections
        foreach (var term in terminations)
        {
            sb.AppendLine(term.IsSource
                ? $"    nw.link('{term.VarName}:0', '{term.PortIndex}:{term.ComponentVar}')"
                : $"    nw.link('{term.ComponentVar}:{term.PortIndex}', '0:{term.VarName}')");
        }

        sb.AppendLine();
    }

    private static string BuildComponentDefinition(Component comp, CultureInfo ci)
    {
        var name = (comp.NazcaFunctionName ?? "").ToLowerInvariant();

        if (name.Contains("dc") || name.Contains("directional"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", ci)})";

        if (name.Contains("mmi") || name.Contains("splitter"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", ci)})  # MMI approximated as 50/50 DC";

        if (name.Contains("phase") || name.Contains("ps"))
            return "pt.Waveguide(length=1e-5, phase=0.0)  # phase shifter as tunable waveguide";

        if (name.Contains("wg") || name.Contains("straight") || name.Contains("waveguide"))
        {
            double lengthMeters = Math.Max(comp.WidthMicrometers * 1e-6, 1e-6);
            return $"pt.Waveguide(length={lengthMeters.ToString("G4", ci)})";
        }

        // GC, Y-branch, and unknowns: fall back to short waveguide so the component has 2 ports.
        // This guarantees the script loads; the user can refine the model post-export.
        int portCount = comp.PhysicalPins?.Count ?? 2;
        return $"pt.Waveguide(length=1e-5)  # stub for {portCount}-port '{name}' — refine manually";
    }

    private static Dictionary<Guid, string> BuildPinToComponentMap(IReadOnlyList<Component> components)
    {
        var map = new Dictionary<Guid, string>();
        foreach (var comp in components)
        {
            if (comp.PhysicalPins == null) continue;
            foreach (var pin in comp.PhysicalPins)
                map[pin.PinId] = comp.Identifier;
        }
        return map;
    }

    private static string? BuildConnectionLink(
        WaveguideConnection conn,
        Dictionary<string, string> nameMap,
        Dictionary<Guid, string> pinToComp,
        Dictionary<Guid, int> pinIndexMap)
    {
        if (!pinToComp.TryGetValue(conn.StartPin.PinId, out var startCompId) ||
            !pinToComp.TryGetValue(conn.EndPin.PinId, out var endCompId))
            return null;

        if (!nameMap.TryGetValue(startCompId, out var startName) ||
            !nameMap.TryGetValue(endCompId, out var endName))
            return null;

        if (!pinIndexMap.TryGetValue(conn.StartPin.PinId, out var startPort) ||
            !pinIndexMap.TryGetValue(conn.EndPin.PinId, out var endPort))
            return null;

        return $"nw.link('{startName}:{startPort}', '{endPort}:{endName}')";
    }

    private static void AppendSimulation(
        StringBuilder sb,
        ExportOptions options,
        int sourceCount,
        int detectorCount,
        CultureInfo ci)
    {
        sb.AppendLine("# ── Simulation ────────────────────────────────────────────────────");
        sb.AppendLine("with pt.Environment(wl=WAVELENGTH_NM * 1e-9):");

        if (sourceCount == 0)
        {
            sb.AppendLine("    print('No unconnected port available for Source — add at least one terminal pin.')");
            return;
        }

        if (options.Mode == SimulationMode.SteadyState)
            AppendSteadyState(sb, sourceCount, detectorCount);
        else
            AppendTimeDomain(sb, options, sourceCount, ci);

        sb.AppendLine();
        sb.AppendLine("plt.tight_layout()");
        sb.AppendLine("plt.show()");
    }

    private static void AppendSteadyState(StringBuilder sb, int sourceCount, int detectorCount)
    {
        sb.AppendLine($"    # Steady-state CW injection at all {sourceCount} source port(s)");
        sb.AppendLine($"    source = torch.ones({sourceCount})");
        sb.AppendLine("    detected = nw(source=source)");
        sb.AppendLine();
        sb.AppendLine("    powers = (detected.abs() ** 2).flatten()");
        sb.AppendLine("    print('Output powers at detectors:', powers.tolist())");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(8, 4))");
        sb.AppendLine("    plt.bar(range(len(powers)), powers.numpy())");
        sb.AppendLine($"    plt.xlabel('Detector index (0..{Math.Max(detectorCount - 1, 0)})')");
        sb.AppendLine("    plt.ylabel('Power')");
        sb.AppendLine("    plt.title('Steady-state detector powers')");
    }

    private static void AppendTimeDomain(
        StringBuilder sb,
        ExportOptions options,
        int sourceCount,
        CultureInfo ci)
    {
        int steps = options.TimeDomainSteps;
        double bitrate = options.BitRateGbps * 1e9;

        sb.AppendLine($"    # Time-domain: {options.BitRateGbps.ToString("G2", ci)} Gbit/s pulse");
        sb.AppendLine($"    nw.bitrate = {bitrate.ToString("G4", ci)}");
        sb.AppendLine($"    pulse = torch.zeros({steps}, {sourceCount})");
        sb.AppendLine($"    pulse[{steps / 10}:{steps / 5}, :] = 1.0  # rising-edge pulse on all sources");
        sb.AppendLine("    detected = nw(source=pulse)");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(10, 4))");
        sb.AppendLine("    plt.plot(detected.numpy())");
        sb.AppendLine("    plt.xlabel('Time step')");
        sb.AppendLine("    plt.ylabel('Amplitude')");
        sb.AppendLine("    plt.title('Time-domain response')");
    }
}

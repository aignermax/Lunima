using System.Globalization;
using System.Numerics;
using System.Text;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Exports Lunima photonic designs to PhotonTorch Python scripts.
/// PhotonTorch is a GPU-accelerated, differentiable photonic circuit simulator
/// built on PyTorch that supports time-domain and steady-state simulation.
/// </summary>
public class PhotonTorchExporter
{
    private const double MetersPerMicrometer = 1e-6;
    private const double DefaultCouplingRatio = 0.5;
    private const double SpeedOfLight = 299792458.0;

    /// <summary>
    /// Options controlling the generated PhotonTorch simulation script.
    /// </summary>
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

    /// <summary>
    /// Simulation mode for the generated PhotonTorch script.
    /// </summary>
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
    /// <param name="components">All components in the design.</param>
    /// <param name="connections">All waveguide connections between components.</param>
    /// <param name="options">Export and simulation options.</param>
    /// <returns>A complete Python script using the PhotonTorch API.</returns>
    public string Export(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        AppendHeader(sb, options, ci);

        var nameMap = BuildComponentNameMap(components);
        AppendComponentDefinitions(sb, components, nameMap, ci);
        AppendNetwork(sb, components, connections, nameMap);
        AppendSimulation(sb, options, nameMap, ci);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ExportOptions options, CultureInfo ci)
    {
        sb.AppendLine("# PhotonTorch export from Lunima");
        sb.AppendLine("# Install: pip install photontorch torch");
        sb.AppendLine("import torch");
        sb.AppendLine("import photontorch as pt");
        sb.AppendLine("import numpy as np");
        sb.AppendLine("import matplotlib.pyplot as plt");
        sb.AppendLine();

        double freqHz = SpeedOfLight / (options.WavelengthNm * 1e-9);
        sb.AppendLine("# ── Simulation parameters ──────────────────────────────────────────");
        sb.AppendLine($"WAVELENGTH_NM = {options.WavelengthNm.ToString("F1", ci)}");
        sb.AppendLine($"FREQ_HZ = {freqHz.ToString("G6", ci)}  # c / {options.WavelengthNm.ToString("F0", ci)}nm");
        sb.AppendLine($"env = pt.Environment(wl={options.WavelengthNm.ToString("G6", ci)}e-9)");
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
        if (name.Contains("y_") || name.Contains("ybranch"))
            return "yb";
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

    private static void AppendComponentDefinitions(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        Dictionary<string, string> nameMap,
        CultureInfo ci)
    {
        sb.AppendLine("# ── Component definitions ──────────────────────────────────────────");
        foreach (var comp in components)
        {
            var varName = nameMap[comp.Identifier];
            var definition = BuildComponentDefinition(comp, ci);
            sb.AppendLine($"{varName} = {definition}");
        }
        sb.AppendLine();
    }

    private static string BuildComponentDefinition(Component comp, CultureInfo ci)
    {
        var name = (comp.NazcaFunctionName ?? "").ToLowerInvariant();

        // Check more-specific patterns before generic ones (e.g. "dc" before "straight")
        if (name.Contains("dc") || name.Contains("directional"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", ci)})";

        if (name.Contains("mmi") || name.Contains("splitter"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", ci)})  # MMI approximated";

        if (name.Contains("gc") || name.Contains("grating"))
            return "pt.Detector()  # grating coupler approximated";

        if (name.Contains("phase") || name.Contains("ps"))
            return "pt.PhaseShifter(phase=0.0)";

        if (name.Contains("y_") || name.Contains("ybranch"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", ci)})  # Y-branch approximated";

        if (name.Contains("wg") || name.Contains("straight") || name.Contains("waveguide"))
        {
            double lengthM = comp.WidthMicrometers * MetersPerMicrometer;
            return $"pt.Waveguide(length={lengthM.ToString("G4", ci)})";
        }

        // Fall back to SMatrix if component has one, otherwise Waveguide stub
        return BuildSMatrixOrStub(comp, ci);
    }

    private static string BuildSMatrixOrStub(Component comp, CultureInfo ci)
    {
        if (comp.WaveLengthToSMatrixMap == null || comp.WaveLengthToSMatrixMap.Count == 0)
        {
            int portCount = comp.PhysicalPins?.Count ?? 2;
            return $"pt.Waveguide(length=1e-5)  # stub for {portCount}-port component";
        }

        return FormatSMatrixComponent(comp, ci);
    }

    private static string FormatSMatrixComponent(Component comp, CultureInfo ci)
    {
        var sb = new StringBuilder();
        sb.AppendLine("pt.Component()  # SMatrix below");
        sb.Append("# s_matrix data: torch.tensor([");

        foreach (var (wl, smat) in comp.WaveLengthToSMatrixMap)
        {
            var matrix = smat.SMat;
            if (matrix == null) continue;

            sb.Append($"  # wavelength {wl}nm\n#   [");
            for (int r = 0; r < matrix.RowCount; r++)
            {
                if (r > 0) sb.Append("#    ");
                sb.Append("[");
                for (int c = 0; c < matrix.ColumnCount; c++)
                {
                    var v = matrix[r, c];
                    if (c > 0) sb.Append(", ");
                    sb.Append($"{v.Real.ToString("G4", ci)}+{v.Imaginary.ToString("G4", ci)}j");
                }
                sb.AppendLine("],");
            }
        }
        sb.Append("])");
        return sb.ToString();
    }

    private static void AppendNetwork(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        Dictionary<string, string> nameMap)
    {
        sb.AppendLine("# ── Network assembly ───────────────────────────────────────────────");
        sb.AppendLine("nw = pt.Network(");

        // Component keyword arguments
        foreach (var comp in components)
        {
            var varName = nameMap[comp.Identifier];
            sb.AppendLine($"    {varName}={varName},");
        }

        // Connections
        sb.AppendLine("    connections=[");

        // Build a lookup: PinId → component identifier
        var pinToComp = BuildPinToComponentMap(components);

        foreach (var conn in connections)
        {
            AppendConnectionLine(sb, conn, nameMap, pinToComp);
        }

        sb.AppendLine("    ],");
        sb.AppendLine(")");
        sb.AppendLine();
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

    private static void AppendConnectionLine(
        StringBuilder sb,
        WaveguideConnection conn,
        Dictionary<string, string> nameMap,
        Dictionary<Guid, string> pinToComp)
    {
        var startPin = conn.StartPin;
        var endPin = conn.EndPin;

        if (!pinToComp.TryGetValue(startPin.PinId, out var startCompId) ||
            !pinToComp.TryGetValue(endPin.PinId, out var endCompId))
            return;

        if (!nameMap.TryGetValue(startCompId, out var startName) ||
            !nameMap.TryGetValue(endCompId, out var endName))
            return;

        var startPort = startPin.Name ?? "out";
        var endPort = endPin.Name ?? "in";

        sb.AppendLine($"        ('{startName}', '{startPort}', '{endName}', '{endPort}'),");
    }

    private static void AppendSimulation(
        StringBuilder sb,
        ExportOptions options,
        Dictionary<string, string> nameMap,
        CultureInfo ci)
    {
        sb.AppendLine("# ── Simulation ─────────────────────────────────────────────────────");
        sb.AppendLine("with pt.Environment(wl=WAVELENGTH_NM * 1e-9):");

        if (options.Mode == SimulationMode.SteadyState)
        {
            AppendSteadyState(sb, nameMap);
        }
        else
        {
            AppendTimeDomain(sb, options, nameMap, ci);
        }

        sb.AppendLine();
        sb.AppendLine("plt.tight_layout()");
        sb.AppendLine("plt.show()");
    }

    private static void AppendSteadyState(StringBuilder sb, Dictionary<string, string> nameMap)
    {
        int portCount = nameMap.Count > 0 ? 1 : 0;
        sb.AppendLine("    # Steady-state: inject at port 0");
        sb.AppendLine($"    source = torch.zeros({Math.Max(portCount, 1)})");
        sb.AppendLine("    source[0] = 1.0  # unit power at first port");
        sb.AppendLine("    detected = nw(source=source)");
        sb.AppendLine();
        sb.AppendLine("    print('Output powers:')");
        sb.AppendLine("    print(detected.abs()**2)");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(8, 4))");
        sb.AppendLine("    plt.bar(range(len(detected.flatten())), (detected.abs()**2).flatten().numpy())");
        sb.AppendLine("    plt.xlabel('Port index')");
        sb.AppendLine("    plt.ylabel('Power')");
        sb.AppendLine("    plt.title('Steady-state output powers')");
    }

    private static void AppendTimeDomain(
        StringBuilder sb,
        ExportOptions options,
        Dictionary<string, string> nameMap,
        CultureInfo ci)
    {
        int steps = options.TimeDomainSteps;
        double bitrate = options.BitRateGbps * 1e9;

        sb.AppendLine($"    # Time-domain: {options.BitRateGbps.ToString("G2", ci)} Gbit/s pulse");
        sb.AppendLine($"    nw.bitrate = {bitrate.ToString("G4", ci)}");
        sb.AppendLine($"    pulse = torch.zeros({steps})");
        sb.AppendLine($"    pulse[{steps / 10}:{steps / 5}] = 1.0  # rising edge pulse");
        sb.AppendLine("    detected = nw(source=pulse)");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(10, 4))");
        sb.AppendLine("    plt.plot(detected.numpy())");
        sb.AppendLine("    plt.xlabel('Time step')");
        sb.AppendLine("    plt.ylabel('Amplitude')");
        sb.AppendLine("    plt.title('Time-domain response')");
    }
}

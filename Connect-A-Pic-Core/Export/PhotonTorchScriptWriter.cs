using System.Globalization;
using System.Text;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Export;

/// <summary>
/// Assembles the Python script body for <see cref="PhotonTorchExporter"/>.
/// Kept in a separate file to keep <see cref="PhotonTorchExporter"/> under
/// the 250-line limit from CLAUDE.md §1.
/// </summary>
internal sealed class PhotonTorchScriptWriter
{
    private const double DefaultCouplingRatio = 0.5;
    private const double SpeedOfLight = 299792458.0;

    private readonly CultureInfo _ci;

    internal PhotonTorchScriptWriter(CultureInfo ci) => _ci = ci;

    internal string Write(
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        PhotonTorchExporter.ExportOptions options,
        ComponentNameMap nameMap,
        Dictionary<Guid, int> pinIndexMap,
        List<Termination> terminations)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, options);
        AppendNetworkBlock(sb, components, connections, nameMap, pinIndexMap, terminations);
        AppendSimulation(sb, options, sourceCount: 1);
        return sb.ToString();
    }

    private void AppendHeader(StringBuilder sb, PhotonTorchExporter.ExportOptions options)
    {
        sb.AppendLine("# PhotonTorch export from Lunima");
        sb.AppendLine("# Install: pip install 'torch<1.9' photontorch matplotlib");
        sb.AppendLine("# NOTE: photontorch 0.4.1 uses torch.solve, removed in torch 1.9+ in favour of torch.linalg.solve.");
        sb.AppendLine("#       Install torch<1.9 or a patched photontorch to run the simulation.");
        sb.AppendLine("import torch");
        sb.AppendLine("import photontorch as pt");
        sb.AppendLine("import matplotlib.pyplot as plt");
        sb.AppendLine();

        double freqHz = SpeedOfLight / (options.WavelengthNm * 1e-9);
        sb.AppendLine("# ── Wavelength settings ───────────────────────────────────────────");
        sb.AppendLine($"WAVELENGTH_NM = {options.WavelengthNm.ToString("F1", _ci)}");
        sb.AppendLine($"FREQ_HZ = {freqHz.ToString("G6", _ci)}  # c / {options.WavelengthNm.ToString("F0", _ci)}nm");
        sb.AppendLine();
    }

    private void AppendNetworkBlock(
        StringBuilder sb,
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections,
        ComponentNameMap nameMap,
        Dictionary<Guid, int> pinIndexMap,
        List<Termination> terminations)
    {
        sb.AppendLine("# ── Network assembly ──────────────────────────────────────────────");
        sb.AppendLine("with pt.Network() as nw:");

        foreach (var comp in components)
            sb.AppendLine($"    nw.{nameMap[comp.Identifier]} = {BuildComponentDefinition(comp)}");

        foreach (var term in terminations)
            sb.AppendLine($"    nw.{term.VarName} = pt.{(term.IsSource ? "Source" : "Detector")}()");

        sb.AppendLine();

        foreach (var conn in connections)
            sb.AppendLine($"    {BuildConnectionLink(conn, nameMap, pinIndexMap)}");

        foreach (var term in terminations)
        {
            sb.AppendLine(term.IsSource
                ? $"    nw.link('{term.VarName}:0', '{term.PortIndex}:{term.ComponentVar}')"
                : $"    nw.link('{term.ComponentVar}:{term.PortIndex}', '0:{term.VarName}')");
        }

        sb.AppendLine();
    }

    private string BuildComponentDefinition(Component comp)
    {
        var name = (comp.NazcaFunctionName ?? "").ToLowerInvariant();

        if (name.Contains("dc") || name.Contains("directional"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", _ci)})";

        if (name.Contains("mmi") || name.Contains("splitter"))
            return $"pt.DirectionalCoupler(coupling={DefaultCouplingRatio.ToString("F2", _ci)})  # MMI approximated as 50/50 DC";

        if (name.Contains("phase") || name.Contains("ps"))
            return "pt.Waveguide(length=1e-5, phase=0.0)  # phase shifter as tunable waveguide";

        if (name.Contains("wg") || name.Contains("straight") || name.Contains("waveguide"))
        {
            double lengthMeters = Math.Max(comp.WidthMicrometers * 1e-6, 1e-6);
            return $"pt.Waveguide(length={lengthMeters.ToString("G4", _ci)})";
        }

        throw new InvalidOperationException(
            $"No PhotonTorch mapping for component '{comp.Identifier}' with NazcaFunctionName " +
            $"'{comp.NazcaFunctionName}'. Supported tokens: dc, directional, mmi, splitter, " +
            "phase, ps, wg, straight, waveguide. Extend PhotonTorchScriptWriter.BuildComponentDefinition " +
            "to add new mappings.");
    }

    private static string BuildConnectionLink(
        WaveguideConnection conn,
        ComponentNameMap nameMap,
        Dictionary<Guid, int> pinIndexMap)
    {
        var startCompId = conn.StartPin.ParentComponent?.Identifier
            ?? throw new InvalidOperationException(
                $"Connection start pin '{conn.StartPin.PinId}' has no ParentComponent.");
        var endCompId = conn.EndPin.ParentComponent?.Identifier
            ?? throw new InvalidOperationException(
                $"Connection end pin '{conn.EndPin.PinId}' has no ParentComponent.");

        if (!nameMap.TryGet(startCompId, out var startName))
            throw new InvalidOperationException($"No PhotonTorch name assigned to component '{startCompId}'.");
        if (!nameMap.TryGet(endCompId, out var endName))
            throw new InvalidOperationException($"No PhotonTorch name assigned to component '{endCompId}'.");

        if (!pinIndexMap.TryGetValue(conn.StartPin.PinId, out var startPort))
            throw new InvalidOperationException($"Start pin '{conn.StartPin.PinId}' of '{startCompId}' is not in the pin-index map.");
        if (!pinIndexMap.TryGetValue(conn.EndPin.PinId, out var endPort))
            throw new InvalidOperationException($"End pin '{conn.EndPin.PinId}' of '{endCompId}' is not in the pin-index map.");

        return $"nw.link('{startName}:{startPort}', '{endPort}:{endName}')";
    }

    private void AppendSimulation(
        StringBuilder sb,
        PhotonTorchExporter.ExportOptions options,
        int sourceCount)
    {
        sb.AppendLine("# ── Simulation ────────────────────────────────────────────────────");
        sb.AppendLine("with pt.Environment(wl=WAVELENGTH_NM * 1e-9):");

        if (options.Mode == PhotonTorchExporter.SimulationMode.SteadyState)
            AppendSteadyState(sb, sourceCount);
        else
            AppendTimeDomain(sb, options, sourceCount);

        sb.AppendLine();
        sb.AppendLine("plt.tight_layout()");
        sb.AppendLine("plt.show()");
    }

    private static void AppendSteadyState(StringBuilder sb, int sourceCount)
    {
        sb.AppendLine($"    # Steady-state CW injection at the {sourceCount} Source terminal");
        sb.AppendLine($"    source = torch.ones({sourceCount})");
        sb.AppendLine("    detected = nw(source=source)");
        sb.AppendLine();
        sb.AppendLine("    powers = (detected.abs() ** 2).flatten()");
        sb.AppendLine("    print('Output powers at detectors:', powers.tolist())");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(8, 4))");
        sb.AppendLine("    plt.bar(range(len(powers)), powers.numpy())");
        sb.AppendLine("    plt.xlabel('Detector index')");
        sb.AppendLine("    plt.ylabel('Power')");
        sb.AppendLine("    plt.title('Steady-state detector powers')");
    }

    private void AppendTimeDomain(
        StringBuilder sb,
        PhotonTorchExporter.ExportOptions options,
        int sourceCount)
    {
        int steps = options.TimeDomainSteps;
        double bitrate = options.BitRateGbps * 1e9;

        sb.AppendLine($"    # Time-domain: {options.BitRateGbps.ToString("G2", _ci)} Gbit/s pulse");
        sb.AppendLine($"    nw.bitrate = {bitrate.ToString("G4", _ci)}");
        sb.AppendLine($"    pulse = torch.zeros({steps}, {sourceCount})");
        sb.AppendLine($"    pulse[{steps / 10}:{steps / 5}, :] = 1.0  # rising-edge pulse");
        sb.AppendLine("    detected = nw(source=pulse)");
        sb.AppendLine();
        sb.AppendLine("    plt.figure(figsize=(10, 4))");
        sb.AppendLine("    plt.plot(detected.numpy())");
        sb.AppendLine("    plt.xlabel('Time step')");
        sb.AppendLine("    plt.ylabel('Amplitude')");
        sb.AppendLine("    plt.title('Time-domain response')");
    }
}

using System.Text.Json;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// <c>sweep_spectrum</c>: transmission spectrum of every output over a wavelength range — the
/// Spectrum tab's computation — with peak/dip, extinction and free spectral range per output.
/// </summary>
public sealed class SweepSpectrumTool : IAgentBridgeTool
{
    private const int DefaultSteps = 201;
    private const int MaxReturnedSamples = 401;

    private readonly AgentDesignSession _session;
    private readonly IWavelengthSweepRunner _runner;

    /// <summary>Creates the tool.</summary>
    public SweepSpectrumTool(AgentDesignSession session, IWavelengthSweepRunner? runner = null)
    {
        _session = session;
        _runner = runner ?? new WavelengthSweepRunner();
    }

    /// <inheritdoc/>
    public string Name => "sweep_spectrum";

    /// <inheritdoc/>
    public string Description =>
        "Wavelength sweep from start_nm to end_nm: per output (light source with laser off) the transmission in dB " +
        "at each wavelength, plus max/min, extinction ratio and free spectral range (rings, MZIs). Needs at least " +
        "one laser on (set_light_source).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            start_nm = new { type = "integer", description = "Start wavelength in nm (e.g. 1500)." },
            end_nm = new { type = "integer", description = "End wavelength in nm (e.g. 1600)." },
            steps = new { type = "integer", description = $"Number of samples (default {DefaultSteps}, max {WavelengthSweepConfiguration.MaxStepCount})." },
        },
        required = new[] { "start_nm", "end_nm" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments) => ExecuteCoreAsync(arguments, null);

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments, AgentToolContext context) => ExecuteCoreAsync(arguments, context);

    private async Task<AgentToolResult> ExecuteCoreAsync(JsonElement arguments, AgentToolContext? context)
    {
        var start = (int?)ToolArguments.GetDouble(arguments, "start_nm");
        var end = (int?)ToolArguments.GetDouble(arguments, "end_nm");
        var steps = (int)(ToolArguments.GetDouble(arguments, "steps") ?? DefaultSteps);
        if (start == null || end == null || end <= start)
            return AgentToolResult.Error("Pass start_nm < end_nm.");
        WavelengthSweepConfiguration configuration;
        try { configuration = new WavelengthSweepConfiguration(start.Value, end.Value, steps); }
        catch (ArgumentException ex) { return AgentToolResult.Error(ex.Message); }

        var circuit = SpectrumSweepCircuitFactory.Create(_session.Canvas);
        if (circuit == null)
            return AgentToolResult.Error("The design is empty.");
        if (circuit.Ports.GetAllExternalInputs().Count == 0)
            return AgentToolResult.Error("No laser is on. Switch an input on with set_light_source.");
        var outputs = OutputPins();
        if (outputs.Count == 0)
            return AgentToolResult.Error("No output: switch the laser off at the output couplers (set_light_source enabled=false).");

        var result = await _runner.RunAsync(configuration, circuit.GridManager, circuit.Ports, context?.CancellationToken ?? default);
        var injected = _session.Canvas.Components
            .Where(c => c.IsLightSource && !c.IsLaserOff)
            .Sum(c => c.LaserConfig!.InputPower);
        var wavelengths = result.GetWavelengthValues();
        return AgentToolResult.Json(new
        {
            StartNm = start,
            EndNm = end,
            Steps = configuration.StepCount,
            result.Warnings,
            Outputs = outputs.Select(o => Describe(o.Key, Transmission(result, o.Value, injected), wavelengths)).ToList(),
        });
    }

    /// <summary>Light pins (in-flow ids) of every output coupler, keyed by component id.</summary>
    private Dictionary<string, IReadOnlyList<Guid>> OutputPins() =>
        _session.Canvas.Components
            .Where(c => c.IsLaserOff)
            .ToDictionary(c => c.Component.Identifier, c => (IReadOnlyList<Guid>)c.Component.PhysicalPins
                .Where(p => p.LogicalPin?.MatterType == MatterType.Light)
                .Select(p => p.LogicalPin.IDInFlow)
                .ToList());

    /// <summary>Received power per sample (summed over the output's pins) relative to the injected power.</summary>
    private static double[] Transmission(WavelengthSweepResult result, IReadOnlyList<Guid> pins, double injected) =>
        result.DataPoints
            .Select(point => pins.Sum(pin => point.OutputPowers.TryGetValue(pin, out var power) ? power : 0)
                / Math.Max(injected, double.Epsilon))
            .ToArray();

    private static object Describe(string output, double[] transmission, double[] wavelengthsNm)
    {
        var curve = new TransmissionCurve(Guid.Empty, wavelengthsNm, transmission,
            transmission.All(t => t <= SimulationReport.DarkPower));
        var db = transmission.Select(SimulationReport.Db).ToList();
        var stride = Math.Max(1, (int)Math.Ceiling(db.Count / (double)MaxReturnedSamples));
        var maxIndex = db.IndexOf(db.Max());
        var minIndex = db.IndexOf(db.Min());
        var fsr = CurveFsrAnalyzer.Analyze(curve);
        return new
        {
            Output = output,
            AtNoiseFloor = curve.IsAtNoiseFloor,
            MaxDb = db[maxIndex],
            MaxAtNm = curve.WavelengthsNm[maxIndex],
            MinDb = db[minIndex],
            MinAtNm = curve.WavelengthsNm[minIndex],
            ExtinctionDb = Math.Round(db[maxIndex] - db[minIndex], 2),
            FsrNm = fsr == null ? (double?)null : Math.Round(fsr.MeanFsrNm, 3),
            WavelengthsNm = curve.WavelengthsNm.Where((_, i) => i % stride == 0).ToList(),
            TransmissionDb = db.Where((_, i) => i % stride == 0).ToList(),
        };
    }
}

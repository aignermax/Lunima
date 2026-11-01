using System.Numerics;
using CAP.Avalonia.Services.AgentBridge.Tools.Author;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Analysis;

/// <summary>
/// Turns a <see cref="SimulationResult"/> into the structured answer an agent needs: what
/// went in (enabled light sources), what came out (every output coupler — a light source with
/// its laser off — with received power), where light escapes through unterminated pins, and
/// optionally the power on every wire. Powers are linear relative to a unit laser power and
/// in dB of that; <c>transmission_db</c> is relative to the total injected power.
/// </summary>
public static class SimulationReport
{
    /// <summary>Power below this (−80 dB) is reported as dark.</summary>
    public const double DarkPower = 1e-8;

    private const int Decimals = 4;

    /// <summary>Builds the report for <paramref name="result"/> on <paramref name="canvas"/>.</summary>
    public static object Build(DesignCanvasViewModel canvas, SimulationResult result, bool includeConnections)
    {
        var fields = result.FieldResults ?? new Dictionary<Guid, Complex>();
        var sources = canvas.Components
            .Where(c => c.IsLightSource && !c.IsLaserOff)
            .Select(c => new { Component = c.Component.Identifier, c.LaserConfig!.WavelengthNm, Power = c.LaserConfig.InputPower })
            .ToList();
        var injected = sources.Sum(s => s.Power);

        return new
        {
            result.Success,
            Error = result.ErrorMessage,
            WavelengthsNm = result.WavelengthsUsed,
            Sources = sources,
            InjectedPower = injected,
            Outputs = DescribeOutputs(canvas, fields, injected),
            OpenEnds = DescribeOpenEnds(canvas, fields),
            Connections = includeConnections ? DescribeWires(canvas) : null,
        };
    }

    /// <summary>Received power of every output coupler (laser off), keyed <c>component</c>.</summary>
    public static IReadOnlyList<OutputPower> MeasureOutputs(DesignCanvasViewModel canvas, IReadOnlyDictionary<Guid, Complex> fields) =>
        canvas.Components
            .Where(c => c.IsLaserOff)
            .Select(c => new OutputPower(c.Component.Identifier, LightPins(c.Component)
                .Sum(p => Power(fields, p.LogicalPin.IDInFlow)), c.Name))
            .ToList();

    private static IReadOnlyList<object> DescribeOutputs(
        DesignCanvasViewModel canvas, IReadOnlyDictionary<Guid, Complex> fields, double injected) =>
        canvas.Components.Where(c => c.IsLaserOff).Select(c =>
        {
            var received = LightPins(c.Component).Sum(p => Power(fields, p.LogicalPin.IDInFlow));
            var phase = LightPins(c.Component)
                .Select(p => fields.TryGetValue(p.LogicalPin.IDInFlow, out var a) ? a : Complex.Zero)
                .OrderByDescending(a => a.Magnitude)
                .FirstOrDefault();
            return (object)new
            {
                Component = c.Component.Identifier,
                Name = c.Name,
                Power = Math.Round(received, 8),
                PowerDb = Db(received),
                TransmissionDb = injected > 0 ? Db(received / injected) : (double?)null,
                PhaseRad = received > DarkPower ? Math.Round(phase.Phase, Decimals) : (double?)null,
            };
        }).ToList();

    /// <summary>Unwired optical pins where light leaves the circuit unused (lost power).</summary>
    private static IReadOnlyList<object> DescribeOpenEnds(DesignCanvasViewModel canvas, IReadOnlyDictionary<Guid, Complex> fields)
    {
        var wired = canvas.Connections
            .SelectMany(c => new[] { c.Connection.StartPin, c.Connection.EndPin })
            .ToHashSet();
        return canvas.Components
            .Where(c => !c.IsLightSource)
            .SelectMany(c => ComponentPins.Of(c.Component).Select(pin => (c, pin)))
            .Where(x => !wired.Contains(x.pin.Pin) && x.pin.Pin.LogicalPin?.MatterType == MatterType.Light)
            .Select(x => (x.c, x.pin, Power: Power(fields, x.pin.Pin.LogicalPin.IDOutFlow)))
            .Where(x => x.Power > DarkPower)
            .Select(x => (object)new { Pin = $"{x.c.Component.Identifier}.{x.pin.Name}", Power = Math.Round(x.Power, 8), PowerDb = Db(x.Power) })
            .ToList();
    }

    private static IReadOnlyList<object> DescribeWires(DesignCanvasViewModel canvas)
    {
        var flows = canvas.PowerFlowVisualizer.CurrentResult?.ConnectionFlows;
        return canvas.Connections.Select(c =>
        {
            ConnectionPowerFlowInfo? flow = null;
            if (flows != null && flows.TryGetValue(c.Connection.Id, out var f))
                flow = new ConnectionPowerFlowInfo(Math.Round(f.InputPower, 8), Math.Round(f.OutputPower, 8), Math.Round(f.NormalizedPowerDb, 2));
            return (object)new
            {
                Id = c.Connection.Id,
                From = $"{c.Connection.StartPin.ParentComponent.Identifier}.{c.Connection.StartPin.Name}",
                To = $"{c.Connection.EndPin.ParentComponent.Identifier}.{c.Connection.EndPin.Name}",
                Flow = flow,
            };
        }).ToList();
    }

    private static IEnumerable<PhysicalPin> LightPins(Component component) =>
        component.PhysicalPins.Where(p => p.LogicalPin?.MatterType == MatterType.Light);

    private static double Power(IReadOnlyDictionary<Guid, Complex> fields, Guid flowId) =>
        fields.TryGetValue(flowId, out var amplitude) ? amplitude.Magnitude * amplitude.Magnitude : 0;

    /// <summary>10·log10 of a linear power, floored at −100 dB for dark ports.</summary>
    public static double Db(double linear) => Math.Round(10 * Math.Log10(Math.Max(linear, 1e-10)), 2);
}

/// <summary>Received power of one output coupler.</summary>
/// <param name="Component">Output component id.</param>
/// <param name="Power">Linear received power.</param>
/// <param name="Name">Display name of the output component.</param>
public sealed record OutputPower(string Component, double Power, string? Name = null);

/// <summary>Power entering and leaving one wire, and its level relative to the brightest wire.</summary>
public sealed record ConnectionPowerFlowInfo(double PowerIn, double PowerOut, double RelativeDb);

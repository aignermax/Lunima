using System.Text.Json;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// <c>describe_component_type</c>: what a library part does, read from its own model — for every
/// input pin, where the light goes (transmission in dB and phase per output) at a wavelength,
/// plus footprint, pins, parameters, the wavelengths it is modelled at and its Nazca/gdsfactory
/// cell. Rich part descriptions are what lets an agent pick the right component.
/// </summary>
public sealed class DescribeComponentTypeTool : IAgentBridgeTool
{
    private const int DefaultWavelengthNm = 1550;
    private const double ReportedFloorDb = -60;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public DescribeComponentTypeTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "describe_component_type";

    /// <inheritdoc/>
    public string Description =>
        "Describe one component type from its own model: for every input pin the transmission (dB) and phase to each " +
        "output at wavelength_nm (nearest modelled wavelength), plus footprint, pins, parameters, modelled wavelengths and " +
        "its Nazca/gdsfactory cell. Use it to understand what a part does before placing it.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            type = new { type = "string", description = "Component type name (list_component_types)." },
            pdk = new { type = "string", description = "PDK when the name exists in several." },
            wavelength_nm = new { type = "integer", description = $"Wavelength (default {DefaultWavelengthNm})." },
        },
        required = new[] { "type" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var template = TemplateLookup.Find(_session.Templates,
            ToolArguments.GetString(arguments, "type"), ToolArguments.GetString(arguments, "pdk"), out var error);
        if (template == null)
            return Task.FromResult(AgentToolResult.Error(error!));

        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var modelled = component.WaveLengthToSMatrixMap.Keys.OrderBy(w => w).ToList();
        var requested = (int)(ToolArguments.GetDouble(arguments, "wavelength_nm") ?? DefaultWavelengthNm);
        var used = modelled.Count == 0 ? (int?)null : modelled.MinBy(w => Math.Abs(w - requested));

        return Task.FromResult(AgentToolResult.Json(new
        {
            template.Name,
            Pdk = template.PdkSource,
            template.Category,
            Width = template.WidthMicrometers,
            Height = template.HeightMicrometers,
            NazcaFunction = template.NazcaFunctionName,
            GdsFactoryFunction = template.GdsFactoryFunction,
            ModelledWavelengthsNm = modelled,
            WavelengthNm = used,
            Pins = template.PinDefinitions.Select(p => new { p.Name, OffsetX = p.OffsetX, OffsetY = p.OffsetY, AngleDeg = p.AngleDegrees, Domain = p.Kind.ToString().ToLowerInvariant() }).ToList(),
            Parameters = ComponentDescriber.DescribeParameters(component),
            Transmission = used == null ? null : Transmission(component, used.Value),
        }));
    }

    /// <summary>Input pin → outputs it reaches above −60 dB (dB, phase), strongest first.</summary>
    private static IReadOnlyList<object> Transmission(CAP_Core.Components.Core.Component component, int wavelength)
    {
        var matrix = component.WaveLengthToSMatrixMap[wavelength];
        var values = matrix.NonLinearConnections is { Count: > 0 }
            ? matrix.CreateEvaluatedSnapshot().GetNonNullValues()
            : matrix.GetNonNullValues();
        var pins = component.PhysicalPins.Where(p => p.LogicalPin != null).ToList();
        return pins.Select(input => (object)new
        {
            From = input.Name,
            To = pins
                .Select(output => (output, value: values.TryGetValue((input.LogicalPin.IDInFlow, output.LogicalPin.IDOutFlow), out var v) ? v : System.Numerics.Complex.Zero))
                .Select(x => (x.output.Name, Db: 10 * Math.Log10(Math.Max(x.value.Magnitude * x.value.Magnitude, 1e-12)), Phase: x.value.Phase))
                .Where(x => x.Db > ReportedFloorDb)
                .OrderByDescending(x => x.Db)
                .Select(x => new { Pin = x.Name, TransmissionDb = Math.Round(x.Db, 3), PhaseRad = Math.Round(x.Phase, 4) })
                .ToList(),
        }).ToList();
    }
}

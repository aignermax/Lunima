using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;
using CAP_Core.Routing.WaveguideCompanions;

namespace CAP.Avalonia.Services.WaveguideCompanions;

/// <summary>
/// The Nazca xsections that carry the process companion layers of one export. Every
/// distinct set of (layer, datatype, growx) bands becomes one xsection
/// (<c>nd.add_xsection</c> + <c>nd.add_layer2xsection(..., growx=…)</c>) — exactly Nazca's
/// model of a waveguide cross-section as a core plus layers grown beyond it — and every
/// routed optical segment is drawn a second time through that xsection, so the GDS carries
/// the bands along straights and arcs alike. Names follow the sorted band signatures, so
/// the script is deterministic regardless of canvas order.
/// </summary>
internal sealed class NazcaCompanionXsectionPlan
{
    private const string NamePrefix = "LUNIMA_WG_COMPANIONS_";

    private readonly record struct Band(int Layer, int DataType, double Grow);

    private readonly DesignCanvasViewModel? _canvas;
    private readonly SortedDictionary<string, List<Band>> _bandsBySignature = new(StringComparer.Ordinal);
    private Dictionary<string, string> _nameBySignature = new(StringComparer.Ordinal);

    private NazcaCompanionXsectionPlan(DesignCanvasViewModel? canvas) => _canvas = canvas;

    /// <summary>A plan that draws no companion layers.</summary>
    public static NazcaCompanionXsectionPlan Empty { get; } = new(null);

    /// <summary>True when no exported waveguide carries a companion layer.</summary>
    public bool IsEmpty => _bandsBySignature.Count == 0;

    /// <summary>
    /// Collects the companion bands of every optical connection and frozen path on the canvas
    /// whose endpoints resolve to a process with companion layers.
    /// </summary>
    public static NazcaCompanionXsectionPlan Build(DesignCanvasViewModel canvas)
    {
        if (canvas.WaveguideCompanionSpecProvider == null)
            return Empty;

        var plan = new NazcaCompanionXsectionPlan(canvas);
        foreach (var connVm in canvas.Connections)
        {
            var conn = connVm.Connection;
            plan.Register(conn.StartPin, conn.EndPin, conn.WidthMicrometers);
        }
        foreach (var compVm in canvas.Components)
        {
            if (compVm.Component is ComponentGroup group)
                plan.RegisterGroup(group);
        }
        foreach (var pathVm in canvas.CanvasFrozenPaths)
            plan.Register(pathVm.Path.StartPin, pathVm.Path.EndPin, pathVm.Path.WidthMicrometers);

        plan.AssignNames();
        return plan;
    }

    /// <summary>
    /// The xsection name an optical waveguide between these pins is drawn through a second
    /// time to emit its companion bands, or null when its process declares none (or it is a
    /// metal trace, which never carries optical companion layers).
    /// </summary>
    public string? XsectionFor(PhysicalPin? startPin, PhysicalPin? endPin, double coreWidthMicrometers)
    {
        var signature = SignatureOf(startPin, endPin, coreWidthMicrometers);
        return signature != null && _nameBySignature.TryGetValue(signature, out var name) ? name : null;
    }

    /// <summary>Appends the xsection definitions; emits nothing for an empty plan.</summary>
    public void AppendDefinitions(StringBuilder sb)
    {
        if (IsEmpty)
            return;

        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("# Waveguide companion layers (process cladding / trench / keep-out bands):");
        sb.AppendLine("# Nazca xsections whose layers grow beyond the core by growx µm on each side.");
        foreach (var (signature, bands) in _bandsBySignature)
        {
            var name = _nameBySignature[signature];
            sb.AppendLine($"nd.add_xsection('{name}')");
            foreach (var band in bands)
            {
                sb.AppendLine(
                    $"nd.add_layer2xsection(xsection='{name}', layer=({band.Layer.ToString(ci)}, {band.DataType.ToString(ci)}), " +
                    $"growx={band.Grow.ToString("0.0###", ci)})");
            }
        }
        sb.AppendLine();
    }

    private void RegisterGroup(ComponentGroup group)
    {
        foreach (var frozenPath in group.InternalPaths)
            Register(frozenPath.StartPin, frozenPath.EndPin, frozenPath.WidthMicrometers);
        foreach (var child in group.ChildComponents)
        {
            if (child is ComponentGroup nested)
                RegisterGroup(nested);
        }
    }

    private void Register(PhysicalPin? startPin, PhysicalPin? endPin, double coreWidthMicrometers)
    {
        var signature = SignatureOf(startPin, endPin, coreWidthMicrometers);
        if (signature == null || _bandsBySignature.ContainsKey(signature))
            return;
        _bandsBySignature[signature] = BandsOf(startPin, endPin, coreWidthMicrometers);
    }

    private void AssignNames()
    {
        int index = 0;
        _nameBySignature = _bandsBySignature.Keys.ToDictionary(
            signature => signature, _ => NamePrefix + (++index).ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);
    }

    private string? SignatureOf(PhysicalPin? startPin, PhysicalPin? endPin, double coreWidthMicrometers)
    {
        if (_canvas == null || startPin == null || endPin == null)
            return null;
        if (PinKindHelper.IsElectrical(startPin) && PinKindHelper.IsElectrical(endPin))
            return null;
        var bands = BandsOf(startPin, endPin, coreWidthMicrometers);
        if (bands.Count == 0)
            return null;

        var ci = CultureInfo.InvariantCulture;
        return string.Join("|", bands.Select(b =>
            $"{b.Layer.ToString(ci)}/{b.DataType.ToString(ci)}:{b.Grow.ToString("0.0###", ci)}"));
    }

    private List<Band> BandsOf(PhysicalPin? startPin, PhysicalPin? endPin, double coreWidthMicrometers) =>
        _canvas!.GetWaveguideCompanions(startPin, endPin).Layers
            .Select(l => new Band(l.GdsLayer, l.GdsDataType, Math.Round(l.OffsetFor(coreWidthMicrometers), 4)))
            .ToList();
}

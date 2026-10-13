using System.Globalization;
using System.Text;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Export;
using CAP_Core.Export.InterconnectRouting;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using CAP_Core.Routing.MetalRouting;

namespace CAP.Avalonia.Services;

/// <summary>
/// Die-assembly half of <see cref="SimpleNazcaExporter"/>: one GDS cell per die, in die-local
/// coordinates (the die's lower-left corner at the origin, its outline drawn on
/// <see cref="DieFrameLayer"/>), and an assembly top cell placing every die where it sits on
/// the carrier. Die-to-die facet links are free-space couplings and never become geometry.
/// Single-die designs never reach this path, so their export output is unchanged.
/// </summary>
public partial class SimpleNazcaExporter
{
    /// <summary>GDS layer of the die outline frame (documentation/floorplan layer).</summary>
    public const int DieFrameLayer = 999;

    /// <summary>
    /// Exports a die assembly: every die as its own cell under one top cell, or — with
    /// <paramref name="onlyDie"/> — just that die as the top cell (the "one file per die" export).
    /// </summary>
    public string ExportAssembly(
        DesignCanvasViewModel canvas,
        ComponentGroup? onlyDie = null,
        MetalRoutingSpec? metalSpec = null,
        List<string>? skippedConnections = null,
        IEnumerable<ComponentTemplate>? library = null,
        List<string>? exportWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var sb = new StringBuilder();
        var metal = metalSpec ?? MetalRoutingSpec.Default;
        var interconnectSettings = SettingsSource?.Invoke() ?? new InterconnectSettings();
        var interconnectPlan = NazcaProcessInterconnectPlan.Build(canvas, interconnectSettings);
        var rawCodePlan = NazcaRawCodeCellWriter.BuildPlan(canvas, include: null, library, exportWarnings);
        var wrapperPlan = NazcaPinLabelWrapperWriter.BuildPlan(canvas, include: null, rawCodePlan, library);

        AppendHeader(sb, interconnectSettings, metal);
        interconnectPlan.AppendTo(sb);
        AppendPdkComponentStubs(sb, canvas, include: null, rawCodePlan, wrapperPlan, false);
        NazcaRawCodeCellWriter.AppendCells(sb, rawCodePlan, CultureInfo.InvariantCulture);
        NazcaPinLabelWrapperWriter.AppendCells(sb, wrapperPlan);

        var dies = onlyDie != null ? new[] { onlyDie } : canvas.Dies.RootDies.ToArray();
        for (int i = 0; i < dies.Length; i++)
            AppendDieFunctions(sb, canvas, dies[i], i, metal, interconnectPlan, rawCodePlan, wrapperPlan, skippedConnections);

        if (onlyDie != null)
        {
            sb.AppendLine("def create_design():");
            sb.AppendLine("    design = create_die_0()");
        }
        else
        {
            AppendAssemblyTopCell(sb, canvas, dies, skippedConnections);
        }
        AppendFooter(sb);
        return sb.ToString();
    }

    /// <summary>Python-safe cell name for a die ("Die 2" → "Die_2").</summary>
    public static string DieCellName(ComponentGroup die)
    {
        var chars = die.GroupName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var name = new string(chars).Trim('_');
        return string.IsNullOrEmpty(name) ? "Die" : name;
    }

    private static void AppendDieFunctions(
        StringBuilder sb, DesignCanvasViewModel canvas, ComponentGroup die, int index, MetalRoutingSpec metal,
        NazcaProcessInterconnectPlan interconnectPlan, RawCodeExportPlan rawCodePlan, PinLabelWrapperPlan wrapperPlan,
        List<string>? skippedConnections)
    {
        var ci = CultureInfo.InvariantCulture;
        var members = new HashSet<Component>(die.GetAllComponentsRecursive());
        var cellName = DieCellName(die);
        sb.AppendLine($"# Die '{die.GroupName}' ({die.ProcessBinding?.DisplayName ?? "no process"})");
        var componentNames = AppendComponents(sb, canvas, emitVerification: false, include: members.Contains,
            topCellName: $"{cellName}_content", rawCodePlan: rawCodePlan, wrapperPlan: wrapperPlan,
            functionName: $"create_die_{index}_content");
        AppendGroupFrozenPaths(sb, die, metal.ToTraceStyle(), componentNames, skippedConnections, interconnectPlan);
        sb.AppendLine("    return design");
        sb.AppendLine();

        var outline = DieAssembly.OutlineOf(die);
        string w = outline.Width.ToString("F3", ci), h = outline.Height.ToString("F3", ci);
        sb.AppendLine($"def create_die_{index}():");
        sb.AppendLine($"    content = create_die_{index}_content()");
        sb.AppendLine($"    with nd.Cell(name='{cellName}') as die:");
        sb.AppendLine("        # Die-local coordinates: the die's lower-left corner is the cell origin.");
        sb.AppendLine($"        content.put({(-outline.X).ToString("F3", ci)}, {(outline.Y + outline.Height).ToString("F3", ci)}, 0)");
        sb.AppendLine($"        nd.Polygon(points=[(0, 0), ({w}, 0), ({w}, {h}), (0, {h})], layer=({DieFrameLayer}, 0)).put(0, 0)");
        sb.AppendLine("    return die");
        sb.AppendLine();
    }

    private static void AppendAssemblyTopCell(
        StringBuilder sb, DesignCanvasViewModel canvas, IReadOnlyList<ComponentGroup> dies, List<string>? skippedConnections)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("def create_design():");
        sb.AppendLine("    with nd.Cell(name='ConnectAPIC_Design') as design:");
        sb.AppendLine("        # Assembly: every die placed where it sits on the carrier");
        for (int i = 0; i < dies.Count; i++)
        {
            var outline = DieAssembly.OutlineOf(dies[i]);
            sb.AppendLine($"        create_die_{i}().put({outline.X.ToString("F3", ci)}, {(-(outline.Y + outline.Height)).ToString("F3", ci)}, 0)");
        }
        foreach (var connection in canvas.Connections.Select(c => c.Connection))
        {
            if (connection.IsCrossChipletFacetLink)
            {
                sb.AppendLine("        # Cross-chiplet facet link (free-space edge coupling) - no waveguide exported");
                continue;
            }
            skippedConnections?.Add(
                $"{connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name} → " +
                $"{connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name} (waveguide between dies)");
        }
    }
}

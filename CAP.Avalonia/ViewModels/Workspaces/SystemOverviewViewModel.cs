using System.Collections.ObjectModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>One chiplet (top-level group) of a multi-chiplet design and its process.</summary>
/// <param name="Name">The chiplet's group name.</param>
/// <param name="ProcessName">The fabrication process it is bound to, or a "no process" label.</param>
/// <param name="Group">The chiplet's top-level group, used to select and show it; null in hand-built rows.</param>
public sealed record ChipletSummaryRow(string Name, string ProcessName, ComponentGroup? Group = null);

/// <summary>One cross-chiplet edge-coupler link and its simulated coupling loss.</summary>
/// <param name="Label">"Chiplet A ↔ Chiplet B".</param>
/// <param name="LossText">Coupling loss, e.g. "0.42 dB", or "no coupling".</param>
/// <param name="IsOverBudget">True when the loss exceeds <see cref="ChipletInterfaceChecker.MaxGapLossDecibels"/>.</param>
public sealed record ChipletLinkRow(string Label, string LossText, bool IsOverBudget);

/// <summary>
/// The System workspace's overview strip: the design's chiplets (top-level groups) with
/// their process binding, and every cross-chiplet edge-coupler link with the coupling loss
/// the S-matrix simulation applies to it (<see cref="ChipletEdgeCouplerCoupling.FieldFactor"/>,
/// the same model the Design Checks' chiplet-interface rule uses). Read-only; recomputed
/// by <see cref="Refresh"/> when the System workspace runs or is entered.
/// </summary>
public partial class SystemOverviewViewModel : ObservableObject
{
    private const double DecibelsPerFieldDecade = 20.0;

    /// <summary>The chiplets of the design.</summary>
    public ObservableCollection<ChipletSummaryRow> Chiplets { get; } = new();

    /// <summary>The cross-chiplet edge-coupler links of the design.</summary>
    public ObservableCollection<ChipletLinkRow> Links { get; } = new();

    /// <summary>True when the design has at least one chiplet (top-level group).</summary>
    [ObservableProperty]
    private bool _hasChiplets;

    /// <summary>True when the design has at least one cross-chiplet link.</summary>
    [ObservableProperty]
    private bool _hasLinks;

    /// <summary>Selects a chiplet on the canvas and brings it into view (left rail Chiplets panel).</summary>
    public Action<ComponentGroup>? ChipletActivated { get; set; }

    /// <summary>Selects and shows the chiplet of <paramref name="row"/>.</summary>
    [RelayCommand]
    private void ActivateChiplet(ChipletSummaryRow? row)
    {
        if (row?.Group is { } group)
            ChipletActivated?.Invoke(group);
    }

    /// <summary>Recomputes the chiplet and link rows.</summary>
    /// <param name="topLevelComponents">The design's top-level components.</param>
    /// <param name="connections">The design's canvas connections.</param>
    /// <param name="resolveProcess">The chiplet's process scope (explicit or derived binding); null = unbound.</param>
    /// <param name="wavelengthNm">Simulation wavelength the gap loss is evaluated at.</param>
    public void Refresh(
        IEnumerable<Component> topLevelComponents,
        IEnumerable<WaveguideConnection> connections,
        Func<ComponentGroup, ActiveProcessSelection?> resolveProcess,
        double wavelengthNm)
    {
        ArgumentNullException.ThrowIfNull(topLevelComponents);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(resolveProcess);

        Chiplets.Clear();
        foreach (var group in topLevelComponents.OfType<ComponentGroup>())
        {
            var process = resolveProcess(group)?.DisplayName
                          ?? LocalizationService.Instance.Translate("Workspace.System.NoProcess");
            Chiplets.Add(new ChipletSummaryRow(group.GroupName, process, group));
        }

        Links.Clear();
        foreach (var link in connections.Where(c => c.IsCrossChipletFacetLink))
            Links.Add(ToRow(link, wavelengthNm));

        HasChiplets = Chiplets.Count > 0;
        HasLinks = Links.Count > 0;
    }

    private static ChipletLinkRow ToRow(WaveguideConnection link, double wavelengthNm)
    {
        var label = $"{ChipletNameOf(link.StartPin)} ↔ {ChipletNameOf(link.EndPin)}";
        double field = ChipletEdgeCouplerCoupling.FieldFactor(link, wavelengthNm);
        if (field <= 0)
            return new ChipletLinkRow(label, LocalizationService.Instance.Translate("Workspace.System.NoCoupling"), true);

        // Clamped: a perfect link (field factor 1) must read 0.00 dB, never -0.00 dB.
        double lossDb = Math.Max(0.0, -DecibelsPerFieldDecade * Math.Log10(field));
        var text = string.Format(CultureInfo.CurrentCulture, "{0:0.00} dB", lossDb);
        return new ChipletLinkRow(label, text, lossDb > ChipletInterfaceChecker.MaxGapLossDecibels);
    }

    private static string ChipletNameOf(PhysicalPin? pin)
    {
        var component = pin?.ParentComponent;
        ComponentGroup? top = null;
        for (var group = component?.ParentGroup as ComponentGroup; group != null; group = group.ParentGroup)
            top = group;
        return top?.GroupName ?? component?.Identifier ?? "?";
    }
}

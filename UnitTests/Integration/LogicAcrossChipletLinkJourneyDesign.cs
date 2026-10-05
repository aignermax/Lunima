using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using UnitTests.Components;
using UnitTests.Helpers;

namespace UnitTests.Integration;

/// <summary>
/// Builds the rung 4×6 composition for issue #1430 on one canvas: chiplet A carries
/// the shipped <c>Logic Gate NOT-NAND.lun</c> group read as a NOT gate (input A,
/// threshold 0.375), chiplet B carries the shipped <c>Logic Gate AND-from-NAND.lun</c>
/// group with its persisted AND reading (inputs A/B, threshold 0.25), and each
/// chiplet terminates in a demo-PDK edge coupler whose facet pins abut across the
/// chiplet boundary — the same edge-coupler link recipe as
/// <see cref="ChipletEdgeCouplerJourneyDesign"/> (#1214): coincident opposing facet
/// pins routed through the real router (#923 abutment), internal gate→facet wiring
/// frozen into the chiplets. The NOT output is wired to chiplet A's facet and the
/// AND input A to chiplet B's facet, so the logic signal physically crosses the link.
/// </summary>
public sealed class LogicAcrossChipletLinkJourneyDesign
{
    /// <summary>Wavelength every extraction, simulation and coupling factor uses.</summary>
    public const int WavelengthNm = 1550;
    public const string ChipletAName = "Chiplet A";
    public const string ChipletBName = "Chiplet B";
    public const string NotGateId = ChipletAName + "/NOT";
    public const string AndGateId = ChipletBName + "/AND";
    public const double AndThreshold = 0.25;

    private const string NotExampleFileName = "Logic Gate NOT-NAND.lun";
    private const string AndExampleFileName = "Logic Gate AND-from-NAND.lun";
    private const double NotThreshold = 0.375;
    private const double FacetWireGapMicrometers = 50.0;

    private LogicAcrossChipletLinkJourneyDesign(
        DesignCanvasViewModel canvas,
        ComponentGroup chipletA,
        ComponentGroup chipletB,
        WaveguideConnection link)
    {
        Canvas = canvas;
        ChipletA = chipletA;
        ChipletB = chipletB;
        Link = link;
    }

    public DesignCanvasViewModel Canvas { get; }
    public ComponentGroup ChipletA { get; }
    public ComponentGroup ChipletB { get; }

    /// <summary>The cross-chiplet edge-coupler link between the two facet pins.</summary>
    public WaveguideConnection Link { get; }

    /// <summary>
    /// Builds the full composition: NOT gate loaded onto the canvas and re-read as a
    /// NOT through the real Truth Table panel flow, AND gate loaded from its own
    /// shipped file and added, both chiplets grouped and aligned facet-to-facet, and
    /// the link routed. The gate groups keep their persisted
    /// <see cref="TruthTablePinAssignment"/>s, so the production
    /// <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler"/> picks them
    /// up as nested gates (<see cref="NotGateId"/>, <see cref="AndGateId"/>).
    /// </summary>
    public static async Task<LogicAcrossChipletLinkJourneyDesign> BuildComposedAsync()
    {
        var canvas = new DesignCanvasViewModel();

        // Chiplet A: the shipped NOT/NAND group re-read as a NOT through the real
        // panel extraction, plus an edge coupler whose facet faces right.
        var notGate = await LoadNotGateAsync(canvas);
        var notY = GatePin(notGate, "Y");
        var (edgeA, edgeTemplate) = CreateEdgeCoupler("a_ec", rotateFacetOutward: true);
        var (_, notYy) = notY.GetAbsolutePosition();
        PositionSoPinLandsAt(edgeA, "waveguide",
            RightEdgeOf(notGate) + FacetWireGapMicrometers, notYy);
        canvas.AddComponent(edgeA, edgeTemplate.Name, edgeTemplate.PdkSource);
        Wire(canvas, notY, Pin(edgeA, "waveguide"));
        var chipletA = Group(canvas, ChipletAName, notGate, edgeA);
        PruneNestedGroupPins(chipletA);

        // Chiplet B, far away: an unrotated edge coupler (facet on its left edge)
        // feeding the shipped AND-from-NAND group at its A input.
        var (edgeB, edgeBTemplate) = CreateEdgeCoupler("b_ec", rotateFacetOutward: false);
        canvas.AddComponent(edgeB, edgeBTemplate.Name, edgeBTemplate.PdkSource);
        var andGate = await LoadAndGateAsync();
        var andA = GatePin(andGate, "A");
        var (ecBx, ecBy) = Pin(edgeB, "waveguide").GetAbsolutePosition();
        var (andAx, andAy) = andA.GetAbsolutePosition();
        andGate.MoveGroup(ecBx + FacetWireGapMicrometers - andAx, ecBy - andAy);
        canvas.AddComponent(andGate, null, null);
        Wire(canvas, Pin(edgeB, "waveguide"), andA);
        var chipletB = Group(canvas, ChipletBName, edgeB, andGate);
        PruneNestedGroupPins(chipletB);

        // Align chiplet B so the facets coincide exactly, then route the link.
        var aFiber = ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletA, "a_ec_fiber");
        var bFiber = ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletB, "b_ec_fiber");
        var (ax, ay) = aFiber.GetAbsolutePosition();
        var (bx, by) = bFiber.GetAbsolutePosition();
        chipletB.MoveGroup(ax - bx, ay - by);
        var path = canvas.Router.Route(aFiber, bFiber);
        path.IsBlockedFallback.ShouldBeFalse("the edge-coupler abutment must not fall back to a blocked route");
        path.IsValid.ShouldBeTrue("the edge-coupler abutment must be a valid route");
        var link = canvas.ConnectPinsWithCachedRoute(aFiber, bFiber, path);
        link.ShouldNotBeNull("the cross-chiplet edge-coupler link must be created");
        link!.Connection.IsRouteFrozen = true;

        return new LogicAcrossChipletLinkJourneyDesign(canvas, chipletA, chipletB, link.Connection);
    }

    /// <summary>The group's connectable canvas-side pin behind an exposed gate pin.</summary>
    public static PhysicalPin GatePin(ComponentGroup gate, string pinName) =>
        gate.ExternalPins.Single(p => p.Name == pinName).InternalPin!;

    /// <summary>
    /// Loads the shipped NOT-NAND example onto the canvas and re-reads its group as a
    /// NOT through the real Truth Table panel flow (input A, output Y, bias BIAS,
    /// threshold 0.375, no signal names) — the same recipe
    /// <see cref="LogicNetworkAssemblerExampleTests"/> uses to seed the persisted
    /// roles the assembler consumes. Renames the group to "NOT".
    /// </summary>
    private static async Task<ComponentGroup> LoadNotGateAsync(DesignCanvasViewModel canvas)
    {
        var fileOps = CreateFileOperations(canvas);
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), NotExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue(
            $"the shipped example '{NotExampleFileName}' must load through the real load path");
        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();

        var groupVm = canvas.Components.Single(c => c.Component == group);
        canvas.Selection.SelectSingle(groupVm);
        var vm = new TruthTableViewModel();
        vm.ConfigureForSelection(groupVm, canvas);
        vm.IsGroupSelected.ShouldBeTrue("the loaded gate group must activate the panel");
        foreach (var pin in vm.InputPins)
        {
            pin.IsChecked = pin.PinName == "A";
            pin.SignalName = "";
        }
        vm.OutputPins.Single(p => p.PinName == "Y").IsChecked = true;
        vm.BiasPins.Single(p => p.PinName == "BIAS").IsChecked = true;
        vm.Threshold = NotThreshold;
        await vm.ExtractCommand.ExecuteAsync(null);
        vm.HasResult.ShouldBeTrue("the extraction that seeds the persisted NOT roles must succeed");

        group.GroupName = "NOT";
        group.EnsureSMatrixComputed();
        return group;
    }

    /// <summary>
    /// Loads the shipped AND-from-NAND example on a scratch canvas and returns its
    /// group — the file already carries the persisted AND reading (inputs A/B,
    /// biases BIAS/BIAS2, threshold 0.25), so no re-extraction is needed. Renamed
    /// to "AND".
    /// </summary>
    private static async Task<ComponentGroup> LoadAndGateAsync()
    {
        var scratch = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(scratch);
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), AndExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue(
            $"the shipped example '{AndExampleFileName}' must load through the real load path");
        var group = scratch.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        group.TruthTablePinAssignment.ShouldNotBeNull(
            "the shipped AND-from-NAND example must carry its persisted pin roles");
        group.GroupName = "AND";
        group.EnsureSMatrixComputed();
        return group;
    }

    // rotateFacetOutward gives the chiplet-A orientation (fiber facet right, waveguide pin left).
    private static (Component Component, ComponentTemplate Template) CreateEdgeCoupler(
        string identifier, bool rotateFacetOutward)
    {
        var demoPdk = MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile);
        var template = MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Edge Coupler");
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        component.Identifier = identifier;
        if (rotateFacetOutward)
        {
            ComponentPoseTransform.Rotate90CounterClockwise(component);
            ComponentPoseTransform.Rotate90CounterClockwise(component);
        }
        return (component, template);
    }

    /// <summary>Shifts a not-yet-placed component so one pin lands on the target position.</summary>
    private static void PositionSoPinLandsAt(Component component, string pinName, double x, double y)
    {
        var (px, py) = Pin(component, pinName).GetAbsolutePosition();
        component.PhysicalX += x - px;
        component.PhysicalY += y - py;
    }

    /// <summary>The loaded gate group's right bounding edge (deepest child extent).</summary>
    private static double RightEdgeOf(ComponentGroup group) =>
        group.ChildComponents.Max(RightEdgeOfComponent);

    private static double RightEdgeOfComponent(Component component) =>
        component is ComponentGroup child
            ? child.PhysicalX + child.MinChildOffsetX + child.WidthMicrometers
            : component.PhysicalX + component.WidthMicrometers;

    /// <summary>Connects two pins inside one chiplet with an explicit frozen straight route.</summary>
    private static void Wire(DesignCanvasViewModel canvas, PhysicalPin from, PhysicalPin to)
    {
        var connection = canvas.ConnectPinsWithCachedRoute(
            from, to, MultiProcessChipletJourneyDesign.StraightPath(from, to));
        connection.ShouldNotBeNull($"route {from.Name} -> {to.Name} must be created");
        connection!.Connection.IsRouteFrozen = true;
    }

    /// <summary>
    /// Drops the chiplet-exposed pins that point at the nested gate group: grouping
    /// auto-exposes every free child pin, but the save format cannot restore an
    /// exposed pin whose internal reference is a child GROUP's pin (the restore
    /// looks it up in the child group's <c>PhysicalPins</c>, which only exists after
    /// an S-matrix sync — the load fails with "Internal pin not found"). The gate
    /// pins stay fully available on the gate groups themselves, which is all the
    /// assembler and the wiring need; the chiplet level only keeps the facet pins.
    /// </summary>
    private static void PruneNestedGroupPins(ComponentGroup chiplet)
    {
        var nested = chiplet.ExternalPins
            .Where(p => p.InternalPin?.ParentComponent is ComponentGroup)
            .ToList();
        foreach (var pin in nested)
        {
            chiplet.ExternalPins.Remove(pin);
        }
    }

    /// <summary>Groups the given components (Ctrl+G equivalent) and returns the group.</summary>
    private static ComponentGroup Group(DesignCanvasViewModel canvas, string name, params Component[] children)
    {
        var command = new CreateGroupCommand(
            canvas,
            children.Select(c => canvas.Components.Single(vm => vm.Component == c)).ToList(),
            name);
        command.Execute();
        return command.CreatedGroup.ShouldNotBeNull($"grouping '{name}' must succeed");
    }

    /// <summary>File operations over the full test template library, for example loads.</summary>
    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!);

    private static PhysicalPin Pin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);
}

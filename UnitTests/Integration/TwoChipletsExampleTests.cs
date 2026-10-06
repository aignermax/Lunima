using System.Collections.ObjectModel;
using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Components;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Acceptance for the shipped rung-6 front-door example
/// <c>examples/Two Chiplets - Edge-Coupler Link.lun</c> (issue #1255): opening it from the
/// Home screen must yield two chiplets with their own demo-process bindings, a clean
/// Design Validation (the facets are aligned — zero gap, zero offset), and a simulated
/// output that equals the #1214 journey's exact product of S-matrix magnitudes and wire
/// propagation loss (<see cref="ChipletEdgeCouplerJourneyDesign"/>): an aligned link
/// costs nothing beyond the components themselves.
/// </summary>
public class TwoChipletsExampleTests
{
    private const int WavelengthNm = 1550;
    private const double AmplitudeTolerance = 1e-6;

    [Fact]
    public async Task Example_LoadsTwoBoundChiplets_ValidatesClean_AndSimulatesLosslessLink()
    {
        var examplePath = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), TwoChipletsExampleAuthoringTests.ExampleFileName);
        var demoPdk = MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile);
        var templates = new List<ComponentTemplate>
        {
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Grating Coupler"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Straight Waveguide 100µm"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Edge Coupler"),
        };
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(demoPdk.Name, ProcessFingerprintFactory.From(demoPdk)),
        });

        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas, templates);
        fileOps.ProcessCatalogProvider = () => catalog;
        string? migrationWarning = null;
        fileOps.OnProcessMigrationWarning = w => migrationWarning = w;

        // ── Open through the real load path the Home screen uses.
        (await fileOps.OpenDesignAsCopyAsync(examplePath)).ShouldBeTrue("the shipped example must open");
        await fileOps.PostLoadRouting;
        migrationWarning.ShouldBeNull("the shipped bindings describe the design completely — no migration");

        // ── Two chiplets, each with its own demo-process binding.
        var groups = canvas.Components
            .Select(vm => vm.Component)
            .OfType<ComponentGroup>()
            .ToList();
        groups.Count.ShouldBe(2, "the example is exactly two chiplets");
        var chipletA = groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletAName);
        var chipletB = groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletBName);
        chipletA.ProcessBinding.ShouldNotBeNull("chiplet A must ship with a process binding");
        chipletB.ProcessBinding.ShouldNotBeNull("chiplet B must ship with a process binding");
        chipletA.ProcessBinding.ShouldNotBeSameAs(chipletB.ProcessBinding,
            "each chiplet carries its own binding, not a shared instance");
        chipletA.ProcessBinding!.MemberPdkNames.ShouldContain(demoPdk.Name);
        chipletB.ProcessBinding!.MemberPdkNames.ShouldContain(demoPdk.Name);

        // ── Aligned facets: Design Validation stays clean.
        var panel = RunValidation(canvas, templates, demoPdk);
        var link = canvas.ConnectionManager.Connections.Single();
        panel.Issues.Count(i => i.Type is DesignIssueType.WaveguideBelowMinWidth
                or DesignIssueType.WaveguideSpacingViolation
                or DesignIssueType.BendRadiusBelowProcessMinimum
                or DesignIssueType.PinMismatch)
            .ShouldBe(0, "no per-process DRC rule fires on the aligned example");
        panel.Issues.ShouldNotContain(i => ReferenceEquals(i.Connection, link),
            "the cross-chiplet edge-coupler link must not be flagged as a violation");

        // ── Simulation: the aligned link costs nothing beyond the components and wires.
        double expected = ChipletEdgeCouplerJourneyDesign.GratingCoupling
            * ChipletEdgeCouplerJourneyDesign.WaveguideThrough
            * ChipletEdgeCouplerJourneyDesign.EdgeCoupling
            * ChipletEdgeCouplerJourneyDesign.EdgeCoupling
            * ChipletEdgeCouplerJourneyDesign.WaveguideThrough
            * chipletA.InternalPaths.Aggregate(1.0, (acc, p) => acc * p.TransmissionCoefficient.Magnitude)
            * chipletB.InternalPaths.Aggregate(1.0, (acc, p) => acc * p.TransmissionCoefficient.Magnitude)
            * link.TransmissionCoefficient.Magnitude;

        var fields = await SimulateAsync(canvas, InjectLight("source",
            ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletA, "a_gc_fiber")));
        double output = Amplitude(fields,
            ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletB, "b_wg_b0").LogicalPin!.IDOutFlow);
        output.ShouldBe(expected, AmplitudeTolerance,
            "the loaded example must reproduce the #1214 journey's exact S-matrix × propagation-loss product");
        output.ShouldBeGreaterThan(0, "light actually crosses the chiplet boundary");
    }

    private static (ExternalInput Input, Guid PinIdInFlow) InjectLight(string name, PhysicalPin pin) =>
        (new ExternalInput(name, new LaserType(LightColor.Red), 0, new Complex(1.0, 0), true),
         pin.LogicalPin!.IDInFlow);

    /// <summary>Runs the S-matrix field propagation over everything currently on the canvas.</summary>
    private static async Task<Dictionary<Guid, Complex>> SimulateAsync(
        DesignCanvasViewModel canvas, params (ExternalInput Input, Guid PinIdInFlow)[] inputs)
    {
        var portManager = new PhysicalExternalPortManager();
        foreach (var (input, pinIdInFlow) in inputs)
        {
            portManager.AddLightSource(input, pinIdInFlow);
        }

        var tileManager = new ComponentListTileManager();
        foreach (var viewModel in canvas.Components)
        {
            tileManager.AddComponent(viewModel.Component);
        }

        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var calculator = new GridLightCalculator(new SystemMatrixBuilder(grid), grid);
        return await calculator.CalculateFieldPropagationAsync(new CancellationTokenSource(), WavelengthNm);
    }

    private static double Amplitude(Dictionary<Guid, Complex> fields, Guid pinFlow) =>
        fields.TryGetValue(pinFlow, out var value)
            ? value.Magnitude
            : throw new ShouldAssertException($"pin flow {pinFlow} missing from simulated fields");

    /// <summary>Runs Design Validation wired like MainViewModel.RunDesignChecks (#936).</summary>
    private static DesignValidationViewModel RunValidation(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates, PdkDraft demoPdk)
    {
        string? PdkSourceOf(PhysicalPin? pin) =>
            pin?.ParentComponent is { } component
                ? ComponentPdkSourceResolver.Resolve(component, templates)
                : null;
        var externalPortPins = canvas.Components
            .SelectMany(vm => vm.Component is ComponentGroup group
                ? group.ExternalPins
                : Enumerable.Empty<GroupPin>())
            .Select(pin => pin.InternalPin!)
            .ToList();
        var panel = new DesignValidationViewModel();
        panel.RunValidation(
            canvas.ConnectionManager.Connections,
            allComponents: canvas.Components.Select(vm => vm.Component),
            processLockActive: false,
            externalPortPins: externalPortPins,
            connectionDrcRuleProvider: connection =>
                ConnectionDrcRuleResolver.ResolveForEndpointPdkNames(
                    PdkSourceOf(connection.StartPin), PdkSourceOf(connection.EndPin),
                    new List<PdkDraft> { demoPdk }));
        return panel;
    }

    private static FileOperationsViewModel CreateFileOperations(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(templates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
}

using System.Collections.ObjectModel;
using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-6 kill-review E2E (issue #1208): two chiplets on one canvas coupled through
/// a demo-PDK <b>edge coupler pair</b>, walked as ONE journey with real machinery —
/// build + bind (placement-policy code path), S-matrix simulation across the chiplet
/// boundary, Design Validation of the cross-chiplet link, and the .lun save/load
/// round-trip with a re-simulation. Expected amplitudes are the exact product of the
/// bundled PDK S-matrix magnitudes (<see cref="ChipletEdgeCouplerJourneyDesign"/>).
/// </summary>
public class ChipletEdgeCouplerJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double AmplitudeTolerance = 1e-6;
    private const double PositionTolerance = 1e-9;

    [Fact]
    public async Task TwoChiplets_CoupledViaEdgeCouplers_SimulateDrcAndRoundTrip()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(design.DemoPdk.Name, ProcessFingerprintFactory.From(design.DemoPdk)),
        });

        // ── Step 1: both chiplets carry a process binding, derived through the
        // exact placement-policy code path the UI uses (#935/#938).
        var policy = new PlacementPolicyContext(
            () => ActiveProcessSelection.Playground(),
            () => Array.Empty<string>(),
            component => ComponentPdkSourceResolver.Resolve(component, design.Templates),
            getProcessCatalog: () => catalog);
        foreach (var chiplet in new[] { design.ChipletA, design.ChipletB })
        {
            var (isAllowed, blockReason, derivedBinding) =
                policy.CheckGroupPlacementAt(chiplet, targetGroup: null, chiplet.GroupName);
            isAllowed.ShouldBeTrue($"Step 1: the placement policy must admit chiplet '{chiplet.GroupName}': {blockReason}");
            chiplet.ProcessBinding = derivedBinding;
            derivedBinding!.MemberPdkNames.ShouldContain(design.DemoPdk.Name,
                $"Step 1: chiplet '{chiplet.GroupName}' binds to the demo PDK process");
        }

        // ── Step 2: the edge couplers abut fiber-to-fiber at one coincident pin pair.
        var aFiber = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletA, "a_ec_fiber");
        var bFiber = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_ec_fiber");
        var (ax, ay) = aFiber.GetAbsolutePosition();
        var (bx, by) = bFiber.GetAbsolutePosition();
        bx.ShouldBe(ax, PositionTolerance, "Step 2: the edge-coupler pair abuts at a coincident pin pair (X)");
        by.ShouldBe(ay, PositionTolerance, "Step 2: the edge-coupler pair abuts at a coincident pin pair (Y)");
        design.Canvas.ConnectionManager.Connections.Count.ShouldBe(1,
            "Step 2: exactly the one cross-chiplet link exists at canvas level");

        // ── Step 3: simulation at 1550 nm — light crosses the boundary; the output
        // equals the product of the individual component transmissions.
        var fieldsBefore = await SimulateAsync(design.Canvas,
            InjectLight("source", ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletA, "a_gc_fiber")));
        double outputBefore = Amplitude(fieldsBefore,
            ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_wg_b0").LogicalPin!.IDOutFlow);
        outputBefore.ShouldBe(design.ExpectedOutputAmplitude, AmplitudeTolerance,
            "Step 3: grating × waveguide × edge coupler × edge coupler × waveguide (plus the wires' "
            + "0.5 dB/cm propagation loss) arrives at chiplet B's output");
        outputBefore.ShouldBeGreaterThan(0, "Step 3: light actually crosses the chiplet boundary");

        // ── Step 4: Design Validation — the cross-chiplet link is no DRC violation.
        var panel = RunValidation(design.Canvas, design);
        var link = design.Canvas.ConnectionManager.Connections.Single();
        panel.Issues.Count(i => i.Type is DesignIssueType.WaveguideBelowMinWidth
                or DesignIssueType.WaveguideSpacingViolation
                or DesignIssueType.BendRadiusBelowProcessMinimum
                or DesignIssueType.PinMismatch)
            .ShouldBe(0, "Step 4: no per-process DRC rule fires anywhere in the journey design");
        panel.Issues.ShouldNotContain(i => ReferenceEquals(i.Connection, link),
            "Step 4: the cross-chiplet edge-coupler link must not be flagged as a violation");

        // ── Step 5: save → load — same output power, same process bindings.
        var workDir = Path.Combine(Path.GetTempPath(), "edge-coupler-journey-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "journey.lun");
            await SaveAsync(design.Canvas, design.Templates, catalog, filePath);
            var loadedCanvas = new DesignCanvasViewModel();
            await LoadAsync(loadedCanvas, design.Templates, catalog, filePath);

            var loadedGroups = loadedCanvas.Components
                .Where(c => c.Component is ComponentGroup)
                .Select(c => (ComponentGroup)c.Component)
                .ToList();
            var loadedA = loadedGroups.SingleOrDefault(g => g.Identifier == design.ChipletA.Identifier)
                .ShouldNotBeNull("Step 5: chiplet A identity must survive the round-trip");
            var loadedB = loadedGroups.SingleOrDefault(g => g.Identifier == design.ChipletB.Identifier)
                .ShouldNotBeNull("Step 5: chiplet B identity must survive the round-trip");
            loadedA.ProcessBinding.ShouldNotBeNull("Step 5: chiplet A's process binding survives (#938)");
            loadedA.ProcessBinding!.MemberPdkNames.ShouldContain(design.DemoPdk.Name);
            loadedB.ProcessBinding.ShouldNotBeNull("Step 5: chiplet B's process binding survives (#938)");
            loadedB.ProcessBinding!.MemberPdkNames.ShouldContain(design.DemoPdk.Name);

            var fieldsAfter = await SimulateAsync(loadedCanvas,
                InjectLight("source", ChipletEdgeCouplerJourneyDesign.ExposedPin(loadedA, "a_gc_fiber")));
            double outputAfter = Amplitude(fieldsAfter,
                ChipletEdgeCouplerJourneyDesign.ExposedPin(loadedB, "b_wg_b0").LogicalPin!.IDOutFlow);
            outputAfter.ShouldBe(outputBefore, AmplitudeTolerance,
                "Step 5: the reloaded design simulates to the same output power");
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }

    // ── Journey helpers ─────────────────────────────────────────────────────────

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
        DesignCanvasViewModel canvas, ChipletEdgeCouplerJourneyDesign design)
    {
        string? PdkSourceOf(PhysicalPin? pin) =>
            pin?.ParentComponent is { } component
                ? ComponentPdkSourceResolver.Resolve(component, design.Templates)
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
                    new List<CAP_DataAccess.Components.ComponentDraftMapper.DTOs.PdkDraft> { design.DemoPdk }));
        return panel;
    }

    /// <summary>Saves the design through the real file-operations facade.</summary>
    private static async Task SaveAsync(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates,
        IReadOnlyList<ProcessGroup> catalog, string filePath)
    {
        var saveVm = CreateFileOperations(canvas, templates);
        saveVm.ProcessCatalogProvider = () => catalog;
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        saveVm.FileDialogService = dialog.Object;
        await saveVm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("the journey design file must be written");
    }

    /// <summary>Loads the design through the real file-operations facade.</summary>
    private static async Task LoadAsync(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates,
        IReadOnlyList<ProcessGroup> catalog, string filePath)
    {
        var loadVm = CreateFileOperations(canvas, templates);
        loadVm.ProcessCatalogProvider = () => catalog;
        string? migrationWarning = null;
        loadVm.OnProcessMigrationWarning = w => migrationWarning = w;
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        loadVm.FileDialogService = dialog.Object;
        await loadVm.LoadDesignCommand.ExecuteAsync(null);
        migrationWarning.ShouldBeNull(
            "Step 5: the persisted bindings describe the design completely — no Playground migration (#938)");
    }

    /// <summary>Creates the file-operations facade used for the .lun save/load round-trip.</summary>
    private static FileOperationsViewModel CreateFileOperations(
        DesignCanvasViewModel canvas, List<ComponentTemplate> templates)
    {
        var library = new ObservableCollection<ComponentTemplate>(templates);
        return new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new CAP_Core.Export.SaxExporter(),
            library,
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new CAP_Core.ErrorConsoleService());
    }
}

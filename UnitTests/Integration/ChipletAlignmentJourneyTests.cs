using System.Numerics;
using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1257 (rung 6 kill review): the full fix loop end to end on the #1214
/// journey design (<see cref="ChipletEdgeCouplerJourneyDesign"/>) — misaligned
/// chiplets cost light, Design Checks flag the link and offer the one-click fix,
/// "Align chiplet" restores the light, Undo/Redo move the chiplet and the loss
/// back and forth exactly, and the aligned design survives a real save/load.
/// </summary>
public class ChipletAlignmentJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double Tolerance = 1e-6;
    private const double AxialShiftMicrometers = 10.0;
    private const double LateralShiftMicrometers = 2.0;

    [Fact]
    public async Task Misalign_CheckAlign_UndoRedo_SaveLoad_FullFixLoop()
    {
        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var canvas = design.Canvas;
        var commandManager = new CommandManager();
        var validation = CreateValidation(canvas, commandManager);

        // BuildComposed aligns chiplet B through a direct core MoveGroup, which the
        // canvas VM position doesn't track — sync it once so the save path (which
        // serializes the VM position as CanvasX) matches the physical layout.
        foreach (var componentVm in canvas.Components)
        {
            componentVm.X = componentVm.Component.PhysicalX;
            componentVm.Y = componentVm.Component.PhysicalY;
        }

        // 1. Aligned baseline.
        double amplitude0 = await SimulateOutputAmplitudeAsync(canvas, design.ChipletA, design.ChipletB);
        amplitude0.ShouldBe(design.ExpectedOutputAmplitude, Tolerance,
            "the aligned baseline must transmit exactly as composed");
        double power0 = amplitude0 * amplitude0;

        // 2. Misalign chiplet B through the canvas move path (drag choreography +
        // undoable group move), +10 µm axial gap and +2 µm lateral offset.
        var chipletBVm = canvas.Components.Single(vm => vm.Component == design.ChipletB);
        canvas.BeginDragComponent(chipletBVm);
        canvas.MoveComponent(chipletBVm, AxialShiftMicrometers, LateralShiftMicrometers);
        commandManager.ExecuteCommand(new GroupMoveCommand(
            canvas, new[] { chipletBVm }, AxialShiftMicrometers, LateralShiftMicrometers));
        canvas.EndDragComponent(chipletBVm);
        await canvas.RecalculateRoutesAsync(); // flush the drag's fire-and-forget re-route
        double shiftedX = design.ChipletB.PhysicalX;
        double shiftedY = design.ChipletB.PhysicalY;

        double amplitude1 = await SimulateOutputAmplitudeAsync(canvas, design.ChipletA, design.ChipletB);
        double power1 = amplitude1 * amplitude1;
        double expectedRatio =
            ChipletEdgeCouplerCoupling.PowerCouplingForOffset(LateralShiftMicrometers)
            * ChipletEdgeCouplerCoupling.PowerCouplingForGap(AxialShiftMicrometers, WavelengthNm);
        (power1 / power0).ShouldBe(expectedRatio, Tolerance,
            "the loss must be exactly the Gaussian offset × gap power factors");
        power1.ShouldBeLessThan(power0, "the misalignment must cost light");

        // 3. Design Checks flag the link and offer the fix.
        RunChecks(validation, canvas, design.ChipletA, design.ChipletB);
        validation.Issues.ShouldContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
            "the shifted link must raise a chiplet-interface finding");
        NavigateToChipletIssue(validation);
        validation.IsCurrentIssueAlignable.ShouldBeTrue(
            "the chiplet-interface finding must offer the Align chiplet fix");

        // 4. One-click fix: checks come back clean and the light is restored.
        await validation.AlignChipletCommand.ExecuteAsync(null);
        await canvas.RecalculateRoutesAsync();
        validation.Issues.ShouldBeEmpty("Design Checks must be clean after the alignment, found: "
            + string.Join(" | ", validation.Issues.Select(i => $"{i.Type}: {i.Description}")));
        double alignedX = design.ChipletB.PhysicalX;
        double alignedY = design.ChipletB.PhysicalY;
        double amplitudeAligned = await SimulateOutputAmplitudeAsync(canvas, design.ChipletA, design.ChipletB);
        amplitudeAligned.ShouldBe(amplitude0, Tolerance, "the alignment must restore the light");

        // 5. Undo → shifted position, loss and warning back. Redo → aligned again.
        commandManager.Undo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        design.ChipletB.PhysicalX.ShouldBe(shiftedX, "undo must restore the shifted position exactly");
        design.ChipletB.PhysicalY.ShouldBe(shiftedY);
        (await SimulateOutputAmplitudeAsync(canvas, design.ChipletA, design.ChipletB))
            .ShouldBe(amplitude1, Tolerance, "undo must bring the loss back");
        RunChecks(validation, canvas, design.ChipletA, design.ChipletB);
        validation.Issues.ShouldContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
            "the warning must return after undo");

        commandManager.Redo().ShouldBeTrue();
        await canvas.RecalculateRoutesAsync();
        design.ChipletB.PhysicalX.ShouldBe(alignedX, "redo must re-apply the alignment exactly");
        design.ChipletB.PhysicalY.ShouldBe(alignedY);
        (await SimulateOutputAmplitudeAsync(canvas, design.ChipletA, design.ChipletB))
            .ShouldBe(amplitude0, Tolerance, "redo must restore the light");

        // 6. Save → load through the real file-operations path.
        var (aFiberX, aFiberY) = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletA, "a_ec_fiber")
            .GetAbsolutePosition();
        var (bFiberX, bFiberY) = ChipletEdgeCouplerJourneyDesign.ExposedPin(design.ChipletB, "b_ec_fiber")
            .GetAbsolutePosition();
        var tempFile = Path.Combine(Path.GetTempPath(), $"chiplet_align_{Guid.NewGuid():N}.cappro");
        try
        {
            await SaveToFile(CreateFileOperations(canvas), tempFile);
            var loadCanvas = new DesignCanvasViewModel();
            await LoadFromFile(CreateFileOperations(loadCanvas), tempFile);
            await loadCanvas.RecalculateRoutesAsync();

            var loadedA = LoadedChiplet(loadCanvas, ChipletEdgeCouplerJourneyDesign.ChipletAName);
            var loadedB = LoadedChiplet(loadCanvas, ChipletEdgeCouplerJourneyDesign.ChipletBName);
            ChipletEdgeCouplerJourneyDesign.ExposedPin(loadedA, "a_ec_fiber").GetAbsolutePosition()
                .ShouldBe((aFiberX, aFiberY), "chiplet A's facet must survive save/load");
            ChipletEdgeCouplerJourneyDesign.ExposedPin(loadedB, "b_ec_fiber").GetAbsolutePosition()
                .ShouldBe((bFiberX, bFiberY), "chiplet B's facet must survive save/load");

            var loadedValidation = CreateValidation(loadCanvas, new CommandManager());
            RunChecks(loadedValidation, loadCanvas, loadedA, loadedB);
            loadedValidation.Issues.ShouldBeEmpty("Design Checks must be clean after save/load, found: "
                + string.Join(" | ", loadedValidation.Issues.Select(i => $"{i.Type}: {i.Description}")));
            (await SimulateOutputAmplitudeAsync(loadCanvas, loadedA, loadedB))
                .ShouldBe(amplitude0, Tolerance, "the light must survive save/load");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>Design Checks panel wired like MainViewModel: align, then re-run the checks.</summary>
    private static DesignValidationViewModel CreateValidation(
        DesignCanvasViewModel canvas, CommandManager commandManager)
    {
        var validation = new DesignValidationViewModel();
        var alignmentService = new ChipletAlignmentService(canvas, commandManager);
        validation.AlignChipletHandler = connection =>
        {
            var refusal = alignmentService.TryAlign(connection, WavelengthNm);
            if (refusal != null) return Task.FromResult<string?>(refusal.ToString());
            var groups = canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().ToArray();
            RunChecks(validation, canvas, groups[0], groups[1]);
            return Task.FromResult<string?>(null);
        };
        return validation;
    }

    private static void RunChecks(
        DesignValidationViewModel validation, DesignCanvasViewModel canvas,
        ComponentGroup chipletA, ComponentGroup chipletB) =>
        validation.RunValidation(
            canvas.ConnectionManager.Connections,
            groups: new[] { chipletA, chipletB },
            allComponents: canvas.Components.Select(vm => vm.Component),
            externalPortPins: chipletA.PhysicalPins.Concat(chipletB.PhysicalPins),
            wavelengthNm: WavelengthNm);

    /// <summary>Steps the issue navigation onto the first chiplet-interface finding.</summary>
    private static void NavigateToChipletIssue(DesignValidationViewModel validation)
    {
        for (int i = 0; i < validation.Issues.Count; i++)
        {
            if (validation.Issues[validation.CurrentIndex].Type.ToString().StartsWith("ChipletInterface"))
                return;
            validation.NextIssueCommand.Execute(null);
        }
        throw new ShouldAssertException("no chiplet-interface issue found to navigate to");
    }

    private static ComponentGroup LoadedChiplet(DesignCanvasViewModel canvas, string name) =>
        canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>()
            .Single(g => g.GroupName == name);

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new CAP_Core.Export.SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas),
            null!);

    private static async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("the design file must be created during save");
    }

    private static async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }

    /// <summary>Output field amplitude at chiplet B's b_wg_b0 pin with light into a_gc_fiber.</summary>
    private static async Task<double> SimulateOutputAmplitudeAsync(
        DesignCanvasViewModel canvas, ComponentGroup chipletA, ComponentGroup chipletB)
    {
        var source = ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletA, "a_gc_fiber");
        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("source", new LaserType(LightColor.Red), 0, new Complex(1.0, 0), true),
            source.LogicalPin!.IDInFlow);

        var tileManager = new ComponentListTileManager();
        foreach (var viewModel in canvas.Components)
        {
            tileManager.AddComponent(viewModel.Component);
        }

        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var calculator = new GridLightCalculator(new SystemMatrixBuilder(grid), grid);
        var fields = await calculator.CalculateFieldPropagationAsync(new CancellationTokenSource(), WavelengthNm);

        var outFlow = ChipletEdgeCouplerJourneyDesign.ExposedPin(chipletB, "b_wg_b0").LogicalPin!.IDOutFlow;
        return fields.TryGetValue(outFlow, out var value)
            ? value.Magnitude
            : throw new ShouldAssertException($"pin flow {outFlow} missing from simulated fields");
    }
}

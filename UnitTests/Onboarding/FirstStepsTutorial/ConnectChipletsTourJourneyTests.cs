using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Onboarding.FirstStepsTutorial;

/// <summary>
/// Acceptance for the "Connect two chiplets" guided tour (issue #1288, slice 4
/// of #769): walking the steps with the real shipped example and the real
/// commands — simulate, move the receiver 5 µm through the tour's "do it for
/// me" button, Design Checks flag the facet gap, "Align chiplet" fixes it —
/// ends with the chiplets aligned and the received power back to the initial
/// value within 1e-9 relative.
/// </summary>
public class ConnectChipletsTourJourneyTests
{
    private const int WavelengthNm = 1550;
    private const double AmplitudeTolerance = 1e-6;
    private const double RelativePowerTolerance = 1e-9;

    [Fact]
    public async Task FullTour_SimulateMoveCheckAlign_EndsAligned_WithPowerRestored()
    {
        var canvas = new DesignCanvasViewModel();
        var commandManager = new CommandManager();
        var validation = CreateValidation(canvas, commandManager);
        await TwoChipletsExampleLoader.LoadAsync(canvas, ConnectChipletsTourViewModel.ExampleFileName);
        var (chipletA, chipletB) = Chiplets(canvas);

        var tour = new ConnectChipletsTourViewModel(canvas, validation, MakeDock(), commandManager);
        tour.Start();
        tour.CurrentStepIndex.ShouldBe(0, "the tour opens on the intro card");

        // Step 1 → 2: run the simulation; capture the aligned baseline power.
        tour.NextCommand.Execute(null);
        double amplitude0 = await SimulateOutputAmplitudeAsync(canvas, chipletA, chipletB);
        amplitude0.ShouldBeGreaterThan(0, "the aligned example must transmit light");
        canvas.ShowPowerFlow = true;
        tour.CurrentStepIndex.ShouldBe(2, "the finished simulation must advance to the move step");

        // Step 2 → 3: the "do it for me" button shifts the receiver 5 µm.
        double receiverX = chipletB.PhysicalX;
        double receiverY = chipletB.PhysicalY;
        tour.MoveReceiverForMeCommand.Execute(null);
        await canvas.RecalculateRoutesAsync();
        double movedDistance = Math.Sqrt(
            Math.Pow(chipletB.PhysicalX - receiverX, 2) + Math.Pow(chipletB.PhysicalY - receiverY, 2));
        movedDistance.ShouldBe(ConnectChipletsTourViewModel.MoveDistanceMicrometers, AmplitudeTolerance,
            "the helper must shift the receiver exactly 5 µm along the facet axis");
        tour.CurrentStepIndex.ShouldBe(3, "the move must advance to the Design Checks step");

        double amplitudeMoved = await SimulateOutputAmplitudeAsync(canvas, chipletA, chipletB);
        (amplitudeMoved * amplitudeMoved).ShouldBeLessThan(amplitude0 * amplitude0,
            "the 5 µm facet gap must cost received power");

        // Step 3 → 4: Design Checks flag the cross-chiplet link.
        RunChecks(validation, canvas, chipletA, chipletB);
        validation.Issues.ShouldContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
            "the 5 µm gap must raise a chiplet-interface finding");
        tour.CurrentStepIndex.ShouldBe(4, "the finding must advance to the Align step");
        NavigateToChipletIssue(validation);
        validation.IsCurrentIssueAlignable.ShouldBeTrue();

        // Step 4 → 5: one click aligns; the checks come back clean.
        await validation.AlignChipletCommand.ExecuteAsync(null);
        await canvas.RecalculateRoutesAsync();
        validation.Issues.ShouldBeEmpty("Design Checks must be clean after the alignment");
        tour.CurrentStepIndex.ShouldBe(5, "the clean checks must advance to the closing words");

        double amplitudeAligned = await SimulateOutputAmplitudeAsync(canvas, chipletA, chipletB);
        amplitudeAligned.ShouldBe(amplitude0, amplitude0 * RelativePowerTolerance,
            "the received power must return to the initial value within 1e-9 relative");

        tour.NextCommand.Execute(null);
        tour.IsCompleted.ShouldBeTrue();
        tour.IsActive.ShouldBeFalse();
    }

    private static (ComponentGroup A, ComponentGroup B) Chiplets(DesignCanvasViewModel canvas)
    {
        var groups = canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().ToList();
        return (groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletAName),
            groups.Single(g => g.GroupName == ChipletEdgeCouplerJourneyDesign.ChipletBName));
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
            var (a, b) = Chiplets(canvas);
            RunChecks(validation, canvas, a, b);
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

    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CommandManager()));
}

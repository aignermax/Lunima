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
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1430 (rung 4×6): a logic signal crossing a chiplet edge-coupler link.
/// Chiplet A carries the shipped NOT gate, chiplet B the shipped AND gate, and the
/// NOT output is wired to chiplet A's edge-coupler facet while the AND input A is
/// wired to chiplet B's facet — so the signal physically crosses the same link the
/// shipped <c>Two Chiplets - Edge-Coupler Link.lun</c> uses. Detection only: the
/// journey pins down where the rung-4 logic layer and the rung-6 chiplet physics
/// fail to compose. Today the <see cref="LogicNetworkAssembler"/> cannot cross the
/// link — it silently drops it (the receiving AND input degenerates into a network
/// input, no error, no warning) — and no level report carries the link's coupling
/// loss, so a misaligned link never flags at the logic layer.
/// </summary>
public class LogicAcrossChipletLinkJourneyTests
{
    private const int WavelengthNm = LogicAcrossChipletLinkJourneyDesign.WavelengthNm;
    private const double Tolerance = 1e-6;
    private const double AxialShiftMicrometers = 10.0;
    private const double LateralShiftMicrometers = 2.0;
    private const string NotInput = LogicAcrossChipletLinkJourneyDesign.NotGateId + ".A";
    private const string NotOutput = LogicAcrossChipletLinkJourneyDesign.NotGateId + ".Y";
    private const string AndOutput = LogicAcrossChipletLinkJourneyDesign.AndGateId + ".Y";

    /// <summary>
    /// The step-2 assertion of the journey, pinned as the minimal repro: the network
    /// must assemble across the link (NOT.Y drives AND.A, so Y = NOT A AND B).
    /// </summary>
    [Fact(Skip = "repro #1430: the LogicNetworkAssembler cannot cross a chiplet edge-coupler link — " +
        "the multi-hop path gate→facet→link→facet→gate resolves no driver, so the receiving AND " +
        "input silently degenerates into a network input (documented by " +
        nameof(Assembler_TodayDropsTheCrossChipletLinkSilently) + ")")]
    public async Task AssembledNetwork_CrossesTheLink_TruthTableIsNotAAndB()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var network = await AssembleAsync(design.Canvas);

        network.InputPinNames.ShouldBe(new[] { NotInput, "B" }, ignoreOrder: true,
            "AND.A is driven by NOT.Y across the link; only the NOT input and AND.B stay network inputs");
        foreach (var a in new[] { false, true })
        foreach (var b in new[] { false, true })
        {
            network.Evaluate(Bits((NotInput, a), ("B", b)))[AndOutput]
                .ShouldBe(!a && b, $"Y = NOT A AND B for A={a}, B={b}");
        }
    }

    /// <summary>
    /// Documents the current wrong value behind the repro: the assembler drops the
    /// link without a complaint — AND.A becomes a third network input, the NOT
    /// output dangles as a tap, and no fan-out warning mentions the crossing.
    /// </summary>
    [Fact]
    public async Task Assembler_TodayDropsTheCrossChipletLinkSilently()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var network = await AssembleAsync(design.Canvas);

        network.InputPinNames.ShouldBe(new[] { NotInput, "A", "B" }, ignoreOrder: true,
            "defect: the AND input fed through the link degenerates into a network input");
        network.FanOutWarnings.ShouldBeEmpty(
            "defect: nothing warns that the signal crossing the chiplet link was dropped");
        foreach (var notA in new[] { false, true })
        foreach (var andA in new[] { false, true })
        foreach (var b in new[] { false, true })
        {
            var outputs = network.Evaluate(Bits((NotInput, notA), ("A", andA), ("B", b)));
            outputs[NotOutput].ShouldBe(!notA, "the NOT gate itself evaluates");
            outputs[AndOutput].ShouldBe(andA && b,
                "defect: the AND reads its degenerate network input, never the NOT output");
        }
    }

    /// <summary>
    /// Steps 3+4: the physics is honest — misaligning chiplet B by the #1257 offset
    /// drops the power arriving at the AND input below its gate threshold, and
    /// Design Checks flag the link — but the logic layer has no level report for the
    /// crossing signal, so nothing flags the degraded 1 there.
    /// </summary>
    [Fact]
    public async Task MisalignedLink_ArrivalPowerFallsBelowGateThreshold_LogicLayerNeverSeesIt()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var commandManager = new CommandManager();
        var network = await AssembleAsync(design.Canvas);
        double aligned = ArrivalPowerAtAndInput(design, network);
        aligned.ShouldBeGreaterThanOrEqualTo(LogicAcrossChipletLinkJourneyDesign.AndThreshold,
            "aligned, the NOT 1-level must reach the AND input above its threshold");

        await MisalignChipletB(design, commandManager);

        double eta = ChipletEdgeCouplerCoupling.PowerCouplingForOffset(LateralShiftMicrometers)
            * ChipletEdgeCouplerCoupling.PowerCouplingForGap(AxialShiftMicrometers, WavelengthNm);
        ChipletEdgeCouplerCoupling.FieldFactor(design.Link, WavelengthNm)
            .ShouldBe(Math.Sqrt(eta), Tolerance, "the link's field factor follows the facet physics");
        double misaligned = ArrivalPowerAtAndInput(design, network);
        misaligned.ShouldBe(aligned * eta, Tolerance,
            "the arrival power drops by exactly the link's coupling loss");
        misaligned.ShouldBeLessThan(LogicAcrossChipletLinkJourneyDesign.AndThreshold,
            "misaligned, the 1-level no longer reaches the AND threshold");

        var validation = CreateValidation(design.Canvas, commandManager);
        RunChecks(validation, design);
        validation.Issues.ShouldContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
            "Design Checks flag the lossy link");

        network.Evaluate(Bits((NotInput, false), ("A", true), ("B", true)))[AndOutput]
            .ShouldBeTrue("defect: the idealized logic layer still reads a silent 1");
        network.FanOutWarnings.ShouldBeEmpty(
            "defect: no level report carries the link loss, so nothing flags the degraded 1");
    }

    /// <summary>
    /// Step 5: the one-click Align chiplet fix restores the link level and clears
    /// the chiplet-interface findings.
    /// </summary>
    [Fact]
    public async Task AlignChiplet_RestoresTheLinkLevel_AndClearsDesignChecks()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var commandManager = new CommandManager();
        var network = await AssembleAsync(design.Canvas);
        double aligned = ArrivalPowerAtAndInput(design, network);
        var validation = CreateValidation(design.Canvas, commandManager);

        await MisalignChipletB(design, commandManager);
        RunChecks(validation, design);
        NavigateToChipletIssue(validation);
        validation.IsCurrentIssueAlignable.ShouldBeTrue(
            "the chiplet-interface finding must offer the Align chiplet fix");

        await validation.AlignChipletCommand.ExecuteAsync(null);
        await design.Canvas.RecalculateRoutesAsync();

        ChipletEdgeCouplerCoupling.FieldFactor(design.Link, WavelengthNm)
            .ShouldBe(1.0, Tolerance, "the aligned link couples perfectly again");
        ArrivalPowerAtAndInput(design, network).ShouldBe(aligned, Tolerance,
            "the alignment restores the arrival level exactly");
        validation.Issues.ShouldNotContain(i => i.Type.ToString().StartsWith("ChipletInterface"),
            "the alignment clears the chiplet-interface findings");
    }

    /// <summary>Step 6: the composed design survives a real save/load and re-assembles identically.</summary>
    [Fact]
    public async Task SaveLoad_RebuildsIdenticalNetwork()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var network = await AssembleAsync(design.Canvas);
        // MoveGroup does not update the canvas VM positions the save path serializes.
        foreach (var componentVm in design.Canvas.Components)
        {
            componentVm.X = componentVm.Component.PhysicalX;
            componentVm.Y = componentVm.Component.PhysicalY;
        }
        var tempFile = Path.Combine(Path.GetTempPath(), $"logic_chiplet_link_{Guid.NewGuid():N}.cappro");
        try
        {
            await SaveToFile(CreateFileOperations(design.Canvas), tempFile);
            var loadCanvas = new DesignCanvasViewModel();
            await LoadFromFile(CreateFileOperations(loadCanvas), tempFile);
            await loadCanvas.RecalculateRoutesAsync();

            var reloaded = await AssembleAsync(loadCanvas);
            reloaded.InputPinNames.ShouldBe(network.InputPinNames, ignoreOrder: true);
            reloaded.OutputPinNames.ShouldBe(network.OutputPinNames, ignoreOrder: true);
            foreach (var notA in new[] { false, true })
            foreach (var andA in new[] { false, true })
            foreach (var b in new[] { false, true })
            {
                var bits = Bits((NotInput, notA), ("A", andA), ("B", b));
                reloaded.Evaluate(bits).ShouldBe(network.Evaluate(bits),
                    $"truth table identical after save/load for {NotInput}={notA}, A={andA}, B={b}");
            }
            WeakestNotOnePower(reloaded).ShouldBe(WeakestNotOnePower(network), Tolerance,
                "the NOT gate's 1-level is identical after save/load");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>Runs the production assembler over the canvas's components and connections.</summary>
    private static Task<LogicNetworkEvaluator> AssembleAsync(DesignCanvasViewModel canvas) =>
        new LogicNetworkAssembler().AssembleAsync(
            canvas.Components.Select(vm => vm.Component).ToList(),
            canvas.ConnectionManager.Connections,
            WavelengthNm);

    /// <summary>
    /// The normalized power physically arriving at the AND input A when the NOT
    /// drives a 1: the gate's weakest 1-level attenuated by the frozen facet wiring
    /// of both chiplets and the link's facet coupling factor.
    /// </summary>
    private static double ArrivalPowerAtAndInput(
        LogicAcrossChipletLinkJourneyDesign design, LogicNetworkEvaluator network)
    {
        double wireField = WireField(design.ChipletA) * WireField(design.ChipletB);
        double linkField = ChipletEdgeCouplerCoupling.FieldFactor(design.Link, WavelengthNm);
        double field = Math.Sqrt(WeakestNotOnePower(network)) * wireField * linkField;
        return field * field;
    }

    /// <summary>The NOT gate's weakest 1-level power, straight from its assembled table.</summary>
    private static double WeakestNotOnePower(LogicNetworkEvaluator network) =>
        network.Gates[LogicAcrossChipletLinkJourneyDesign.NotGateId].TruthTable.Rows
            .Where(row => row.Outputs["Y"].IsOne)
            .Min(row => row.Outputs["Y"].Power);

    /// <summary>Field transmission of a chiplet's frozen internal wiring.</summary>
    private static double WireField(ComponentGroup chiplet) =>
        chiplet.InternalPaths.Aggregate(1.0, (acc, p) => acc * p.TransmissionCoefficient.Magnitude);

    /// <summary>Misaligns chiplet B through the canvas move path (+10 µm axial, +2 µm lateral).</summary>
    private static async Task MisalignChipletB(
        LogicAcrossChipletLinkJourneyDesign design, CommandManager commandManager)
    {
        var chipletBVm = design.Canvas.Components.Single(vm => vm.Component == design.ChipletB);
        design.Canvas.BeginDragComponent(chipletBVm);
        design.Canvas.MoveComponent(chipletBVm, AxialShiftMicrometers, LateralShiftMicrometers);
        commandManager.ExecuteCommand(new GroupMoveCommand(
            design.Canvas, new[] { chipletBVm }, AxialShiftMicrometers, LateralShiftMicrometers));
        design.Canvas.EndDragComponent(chipletBVm);
        await design.Canvas.RecalculateRoutesAsync();
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
            RunChecks(validation, canvas);
            return Task.FromResult<string?>(null);
        };
        return validation;
    }

    private static void RunChecks(
        DesignValidationViewModel validation, LogicAcrossChipletLinkJourneyDesign design) =>
        RunChecks(validation, design.Canvas);

    private static void RunChecks(DesignValidationViewModel validation, DesignCanvasViewModel canvas)
    {
        var groups = canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>().ToArray();
        validation.RunValidation(
            canvas.ConnectionManager.Connections,
            groups: groups,
            allComponents: canvas.Components.Select(vm => vm.Component),
            externalPortPins: groups.SelectMany(g => g.PhysicalPins),
            wavelengthNm: WavelengthNm);
    }

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

    /// <summary>Builds an input-bit dictionary from (name, bit) pairs.</summary>
    private static Dictionary<string, bool> Bits(params (string Name, bool Bit)[] bits) =>
        bits.ToDictionary(pair => pair.Name, pair => pair.Bit);
}

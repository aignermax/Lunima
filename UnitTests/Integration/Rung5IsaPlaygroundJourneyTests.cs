using System.Globalization;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Logic.Isa;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 payoff as one user journey (issue #1230): open the shipped 4-bit adder
/// through the Home-Examples-tile path, Logic tab → Build (real
/// <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler"/>, real hand-over
/// through <see cref="BuiltLogicNetworkProvider"/>), then the ISA playground resolved
/// from DI runs count-to-5 to HALT with the photonic-ADD toggle on — the per-step
/// trace (PC, ACC, RAM) must equal the golden model's. The save-copy → close → reopen
/// → rebuild cycle must reproduce the identical trace, and an unrelated design (MZI)
/// must disable the toggle and fall back to the golden ALU without throwing.
/// </summary>
[Collection("LocalizationSingleton")]
public class Rung5IsaPlaygroundJourneyTests
{
    private const string FourBitAdderFileName = "Logic Gate 4-Bit Adder.lun";
    private const string MziFileName = "Mach-Zehnder Interferometer.lun";

    [Fact]
    public async Task FourBitAdderCountTo5_PhotonicAdd_SurvivesSaveLoad_MziFallsBackToGoldenAlu()
    {
        using var container = BuildContainer();
        var networkProvider = container.GetRequiredService<BuiltLogicNetworkProvider>();
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        var examples = new ExampleDesignsService().GetExamples();

        // Step 1: open the shipped 4-bit adder the way the Home Examples tile does.
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath(examples, FourBitAdderFileName))).ShouldBeTrue();
        await fileOps.PostLoadRouting;

        // Step 2: Logic tab → Build; the network reaches the playground through the provider.
        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);
        networkProvider.Network.ShouldNotBeNull("a successful Logic Build hands the network to the playground");
        PhotonicAdderAlu.Accepts(networkProvider.Network).ShouldBeTrue();
        var gateCount = networkProvider.Network!.Gates.Count;

        // Steps 3+4: the DI-resolved playground opens demo-ready on count-to-5; the
        // photonic toggle is enabled by the built adder network and switches on.
        playground.Samples.ShouldNotBeEmpty();
        playground.SelectedSample.ShouldBe(playground.Samples[0]);
        playground.ProgramText.ShouldContain("count-to-5");
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);
        playground.IsPhotonicToggleEnabled.ShouldBeTrue(
            "the built 4-bit adder must enable the photonic-ADD toggle");
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue();

        // Step 5: run to HALT on the deterministic auto-step loop; the full per-step
        // trace (PC, ACC, RAM) must equal the golden model's run of the same program.
        var photonicTrace = RunToHalt(playground);
        playground.Accumulator.ShouldBe(5);
        var goldenTrace = GoldenModelTrace(playground.ProgramText);
        photonicTrace.ShouldBe(goldenTrace);

        // Step 6: at least one ADD ran photonically — the status names the network.
        playground.PhotonicStatusText.ShouldBe(ExpectedPhotonicStatus(gateCount));

        // Step 7: save as a copy, close, reopen, rebuild — identical trace.
        var savedPath = Path.Combine(Path.GetTempPath(), $"rung5-journey-{Guid.NewGuid():N}.lun");
        try
        {
            await SaveAsCopy(fileOps, savedPath);

            var reopenedCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(savedPath);
            var rebuiltPanel = container.GetRequiredService<LogicPanelViewModel>();
            rebuiltPanel.Configure(reopenedCanvas);
            await rebuiltPanel.BuildNetworkCommand.ExecuteAsync(null);
            rebuiltPanel.HasNetwork.ShouldBeTrue(rebuiltPanel.StatusText);

            // The rebuild clears-then-publishes the provider, so the toggle flipped
            // off; the journey repeats steps 3-5, which includes switching it back on.
            playground.IsPhotonicToggleEnabled.ShouldBeTrue(
                "the rebuilt adder network must re-enable the photonic-ADD toggle");
            playground.AssembleCommand.Execute(null);
            playground.UsePhotonicAdder = true;
            playground.UsePhotonicAdder.ShouldBeTrue();
            var rerunTrace = RunToHalt(playground);
            playground.Accumulator.ShouldBe(5);
            rerunTrace.ShouldBe(goldenTrace, "the save/load/rebuild cycle must not change execution");
            playground.PhotonicStatusText.ShouldBe(ExpectedPhotonicStatus(gateCount));
        }
        finally
        {
            if (File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }
        }

        // Step 8 (negative seam): an unrelated design disables the toggle and the
        // playground falls back to the golden ALU without throwing.
        var mziCanvas = new DesignCanvasViewModel();
        var mziFileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(mziCanvas);
        (await mziFileOps.OpenDesignAsCopyAsync(ExamplePath(examples, MziFileName))).ShouldBeTrue();
        await mziFileOps.PostLoadRouting;
        var mziPanel = container.GetRequiredService<LogicPanelViewModel>();
        mziPanel.Configure(mziCanvas);
        await mziPanel.BuildNetworkCommand.ExecuteAsync(null);
        mziPanel.HasNetwork.ShouldBeFalse("the MZI holds no logic gates, so the build reports a readable failure");
        networkProvider.Network.ShouldBeNull("the failed build clears the hand-off");

        playground.IsPhotonicAddAvailable.ShouldBeFalse();
        playground.IsPhotonicToggleEnabled.ShouldBeFalse();
        playground.UsePhotonicAdder.ShouldBeFalse("losing the adder network falls back to the golden ALU");
        playground.AssembleCommand.Execute(null);
        var fallbackTrace = RunToHalt(playground);
        playground.Accumulator.ShouldBe(5);
        fallbackTrace.ShouldBe(goldenTrace, "the golden-ALU fallback still executes count-to-5 correctly");
        playground.PhotonicStatusText.ShouldBeEmpty("no photonic ADD ran on the golden ALU");
    }

    /// <summary>
    /// The production DI registrations for this journey: the playground and the
    /// <see cref="BuiltLogicNetworkProvider"/> singleton from
    /// <see cref="IsaPlaygroundFeatureExtensions.AddIsaPlaygroundFeature"/>, plus the
    /// transient Logic panel exactly as <c>CanvasAndPanelExtensions</c> registers it.
    /// </summary>
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddIsaPlaygroundFeature();
        services.AddTransient<LogicPanelViewModel>();
        return services.BuildServiceProvider();
    }

    private static string ExamplePath(IReadOnlyList<ExampleDesign> examples, string fileName) =>
        examples.Single(e => Path.GetFileName(e.FilePath) == fileName).FilePath;

    /// <summary>Runs the playground's auto-step loop to HALT, recording PC, ACC and RAM per step.</summary>
    private static List<string> RunToHalt(IsaPlaygroundViewModel vm)
    {
        vm.ToggleRunCommand.Execute(null);
        vm.IsRunning.ShouldBeTrue("Run must start on the assembled program");
        var trace = new List<string>();
        while (vm.IsRunning && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            vm.AdvanceRunTick();
            trace.Add($"PC={vm.ProgramCounter} ACC={vm.Accumulator} RAM=[{vm.RamText}]");
        }

        vm.IsRunning.ShouldBeFalse("count-to-5 must halt within the step cap");
        vm.ErrorText.ShouldBeEmpty();
        return trace;
    }

    /// <summary>The golden model's per-step trace of the same program, formatted as the VM renders it.</summary>
    private static List<string> GoldenModelTrace(string source)
    {
        var program = new IsaAssembler().Assemble(source);
        var emulator = new IsaEmulator(program);
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue("the golden model must halt within the step cap");
        return trace;
    }

    /// <summary>Saves the open design copy to <paramref name="path"/> through the real save path.</summary>
    private static async Task SaveAsCopy(FileOperationsViewModel fileOps, string path)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(path);
        fileOps.FileDialogService = dialog.Object;
        await fileOps.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(path).ShouldBeTrue("the real save path must write the copy");
    }

    private static string ExpectedPhotonicStatus(int gateCount) =>
        string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAdd"),
            gateCount);
}

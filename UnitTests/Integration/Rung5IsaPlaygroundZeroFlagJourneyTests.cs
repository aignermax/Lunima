using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 zero-flag journey (issue #1322, the #1230 journey moved onto the branch
/// decision): open the shipped Zero Detect 4-bit example through the Home-Examples-tile
/// path, Logic tab → Build (real
/// <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler"/>, real hand-over
/// through <see cref="BuiltLogicNetworkProvider"/>), then the DI-resolved ISA playground
/// runs count-to-5 to HALT with the photonic toggle on — the per-step trace (PC, ACC,
/// RAM) must equal the golden model's, and the photonic flag must be consulted exactly
/// once per executed <c>JZ</c>: the program's control flow is decided by light.
/// </summary>
[Collection("LocalizationSingleton")]
public class Rung5IsaPlaygroundZeroFlagJourneyTests
{
    private const string ZeroDetectFileName = "Logic Gate Zero Detect 4-bit.lun";

    [Fact]
    public async Task ZeroDetectCountTo5_PhotonicZeroFlag_DecidesEveryJzOnLight()
    {
        using var container = BuildContainer();
        var networkProvider = container.GetRequiredService<BuiltLogicNetworkProvider>();
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        var examples = new ExampleDesignsService().GetExamples();

        // Step 1: open the shipped Zero Detect example the way the Home Examples tile does.
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath(examples, ZeroDetectFileName))).ShouldBeTrue();
        await fileOps.PostLoadRouting;

        // Step 2: Logic tab → Build; the network reaches the playground through the provider.
        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);
        networkProvider.Network.ShouldNotBeNull("a successful Logic Build hands the network to the playground");
        PhotonicZeroFlag.Accepts(networkProvider.Network).ShouldBeTrue(
            "the Zero Detect network exposes A0–A3 and the Z tap");

        // Step 3: the playground offers the zero-flag toggle (and no ALU misdetection).
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);
        playground.ProgramText.ShouldContain("count-to-5");
        playground.IsPhotonicZeroFlagAvailable.ShouldBeTrue();
        playground.IsPhotonicAddAvailable.ShouldBeFalse();
        playground.IsPhotonicAndAvailable.ShouldBeFalse();
        playground.IsPhotonicNotAvailable.ShouldBeFalse();
        playground.IsPhotonicToggleEnabled.ShouldBeTrue(
            "the built Zero Detect network must enable the photonic toggle");

        // Step 4: toggle on — the header names the zero flag, then run to HALT.
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue();
        playground.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicZeroFlag"));
        playground.ZeroFlag.ShouldNotBeNull("JZ must branch on the photonic zero-detect network");

        var program = new IsaAssembler().Assemble(playground.ProgramText);
        var photonicTrace = RunToHalt(playground, program, out var jzCount);
        playground.Accumulator.ShouldBe(5);
        photonicTrace.ShouldBe(GoldenModelTrace(playground.ProgramText),
            "deciding JZ on light must reproduce the golden model cycle by cycle");
        jzCount.ShouldBeGreaterThan(0, "count-to-5's loop exits on the JZ zero check");
        playground.ZeroFlag.ConsultationCount.ShouldBe(jzCount,
            "the photonic flag must be consulted exactly once per executed JZ");
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

    /// <summary>
    /// Runs the playground's auto-step loop to HALT, recording PC, ACC and RAM per step
    /// and counting the executed JZ instructions (decoded at the pre-step PC).
    /// </summary>
    private static List<string> RunToHalt(
        IsaPlaygroundViewModel vm, byte[] program, out int jzCount)
    {
        vm.ToggleRunCommand.Execute(null);
        vm.IsRunning.ShouldBeTrue("Run must start on the assembled program");
        var trace = new List<string>();
        jzCount = 0;
        while (vm.IsRunning && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            if (IsaInstruction.Decode(program[vm.ProgramCounter], out _)?.Opcode == IsaOpcode.Jz)
            {
                jzCount++;
            }

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
}

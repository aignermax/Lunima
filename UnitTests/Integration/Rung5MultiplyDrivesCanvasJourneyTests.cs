using System.Diagnostics;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
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
/// Rung-5 demo journey as one hard E2E scenario (issue #1250): open the shipped
/// 4-bit adder through the real file path, Logic Build in the Logic panel (network
/// held by <see cref="BuiltLogicNetworkProvider"/>), then the DI-resolved ISA
/// playground runs the <b>Multiply 3 by 4</b> sample with "Compute ADD on the
/// photonic chip" on. After every photonic ADD the Logic panel's A0–A3/B0–B3 toggles
/// must equal that ADD's operands (<see cref="PhotonicAdderAlu.LastAddTrace"/>) with
/// Cin off, the named output chips must read S = (A+B) mod 16 and Cout = carry, and
/// a full-adder group's canvas badge must reflect the new evaluation. The per-step
/// time must stay under the 100 ms UI budget that #1245 pinned. Final state ACC = 12,
/// status halted, trace identical to the golden model — and identical again after a
/// save → reload → rebuild → rerun cycle. With the golden-model ALU the Logic panel's
/// toggles must never change during the run.
/// </summary>
[Collection("LocalizationSingleton")]
public class Rung5MultiplyDrivesCanvasJourneyTests
{
    private const string FourBitAdderFileName = "Logic Gate 4-Bit Adder.lun";
    private const string SampleFileName = "multiply-3x4.asm";
    // Pins the shipped "Logic Gate 4-Bit Adder.lun" example layout: T0H2SUM is the group
    // name of the full-adder chain's SUM output inside that file — if the example is
    // renamed/regrouped, this constant must move with it.
    private const string SumBadgeGroupName = "T0H2SUM";

    /// <summary>The 100 ms UI budget for one photonic step (#1245), asserted on the steady-state median.</summary>
    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    /// <summary>Hard per-step ceiling: CI-runner load may triple a single step without indicating a UI freeze.</summary>
    private static readonly TimeSpan PerStepCeiling = TimeSpan.FromMilliseconds(400);

    [Fact]
    public async Task Multiply3x4_PhotonicAdds_DriveLogicPanelAndCanvas_SurviveSaveLoad()
    {
        CAP.Avalonia.Services.Localization.LocalizationService.Instance.SetLanguage("en");
        using var container = BuildContainer();
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        var examples = new ExampleDesignsService().GetExamples();
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath(examples, FourBitAdderFileName))).ShouldBeTrue();
        await fileOps.PostLoadRouting;

        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);

        playground.SelectedSample = playground.Samples.Single(s => s.FileName == SampleFileName);
        playground.SelectedSample.DisplayName.ShouldBe("Multiply 3 by 4");
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue("the built 4-bit adder must enable the photonic-ADD toggle");

        // Warm-up: the first photonic ADD pays JIT for the ALU and the panel's
        // re-evaluation; the 100 ms UI budget applies to steady state (#1245).
        RunSteps(playground);
        playground.Accumulator.ShouldBe(12);
        playground.ResetCommand.Execute(null);

        var (trace, drivenSequence, sumBadgeBits) = RunMultiplyLockstep(playground, logicPanel, canvas, measure: true);
        playground.Accumulator.ShouldBe(12, "3 x 4 by repeated addition must end with ACC = 12");
        playground.MachineStatusText.ShouldBe("HALTED");
        sumBadgeBits.Distinct().Count().ShouldBeGreaterThan(1,
            "the canvas badge must track the changing evaluation, not stick on one ADD's value");
        var goldenTrace = GoldenModelTrace(playground.ProgramText);
        trace.ShouldBe(goldenTrace, "the photonic run must evolve identically to the golden model");

        var savedPath = Path.Combine(Path.GetTempPath(), $"rung5-multiply-{Guid.NewGuid():N}.lun");
        try
        {
            await SaveAsCopy(fileOps, savedPath);
            var reopenedCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(savedPath);
            var rebuiltPanel = container.GetRequiredService<LogicPanelViewModel>();
            rebuiltPanel.Configure(reopenedCanvas);
            await rebuiltPanel.BuildNetworkCommand.ExecuteAsync(null);
            rebuiltPanel.HasNetwork.ShouldBeTrue(rebuiltPanel.StatusText);

            playground.AssembleCommand.Execute(null);
            playground.UsePhotonicAdder = true;
            playground.UsePhotonicAdder.ShouldBeTrue(
                "the rebuilt adder network must re-enable the photonic-ADD toggle");
            var rerun = RunMultiplyLockstep(playground, rebuiltPanel, reopenedCanvas, measure: false);
            rerun.Trace.ShouldBe(goldenTrace, "the save/load/rebuild cycle must not change execution");
            rerun.DrivenSequence.ShouldBe(drivenSequence,
                "the identical rerun must drive the identical sequence of operand bits");
            rerun.SumBadgeBits.ShouldBe(sumBadgeBits);
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }

    [Fact]
    public async Task Multiply3x4_GoldenModel_NeverTouchesLogicPanel()
    {
        using var container = BuildContainer();
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        var examples = new ExampleDesignsService().GetExamples();
        (await fileOps.OpenDesignAsCopyAsync(ExamplePath(examples, FourBitAdderFileName))).ShouldBeTrue();
        await fileOps.PostLoadRouting;
        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);

        playground.SelectedSample = playground.Samples.Single(s => s.FileName == SampleFileName);
        var before = logicPanel.Inputs.Select(i => i.IsOn).ToArray();
        RunSteps(playground);

        playground.Accumulator.ShouldBe(12, "the golden model still computes the multiplication");
        logicPanel.Inputs.Select(i => i.IsOn).ShouldBe(before,
            "a golden-model run must never touch the Logic tab's toggles");
        playground.PhotonicStatusText.ShouldBeEmpty("no photonic ADD ran on the golden ALU");
    }

    /// <summary>The production DI wiring: playground + provider singleton + transient Logic panel.</summary>
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddIsaPlaygroundFeature();
        services.AddTransient<LogicPanelViewModel>();
        return services.BuildServiceProvider();
    }

    private static string ExamplePath(IReadOnlyList<ExampleDesign> examples, string fileName) =>
        examples.Single(e => Path.GetFileName(e.FilePath) == fileName).FilePath;

    /// <summary>Steps the playground to HALT without checking the Logic panel.</summary>
    private static void RunSteps(IsaPlaygroundViewModel vm)
    {
        var steps = 0;
        while (!vm.MachineStatusText.StartsWith("HALTED") && steps++ < IsaPlaygroundViewModel.MaxRunSteps)
        {
            vm.StepCommand.Execute(null);
        }

        vm.ErrorText.ShouldBeEmpty();
    }

    /// <summary>
    /// Steps the playground's multiply run in lockstep with the golden model. After every
    /// photonic ADD, asserts the Logic panel toggles/outputs and the S0 canvas badge against
    /// <see cref="PhotonicAdderAlu.LastAddTrace"/>. Returns the per-step trace, the driven
    /// operand sequence and the S0 badge bits per ADD.
    /// </summary>
    private static (List<string> Trace, List<string> DrivenSequence, List<bool> SumBadgeBits) RunMultiplyLockstep(
        IsaPlaygroundViewModel playground, LogicPanelViewModel panel, DesignCanvasViewModel canvas, bool measure)
    {
        var words = new IsaAssembler().Assemble(playground.ProgramText);
        var golden = new IsaEmulator(words);
        var trace = new List<string>();
        var driven = new List<string>();
        var badgeBits = new List<bool>();
        var stepTimes = new List<TimeSpan>();
        while (!golden.IsHalted && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            var isAdd = IsaInstruction.Decode(words[golden.ProgramCounter], out _)?.Opcode == IsaOpcode.Add;
            var watch = Stopwatch.StartNew();
            playground.StepCommand.Execute(null);
            watch.Stop();
            if (measure)
            {
                stepTimes.Add(watch.Elapsed);
                watch.Elapsed.ShouldBeLessThan(PerStepCeiling,
                    $"step {trace.Count + 1} exceeded the hard per-step ceiling — the UI thread would visibly freeze");
            }

            golden.Step();
            trace.Add($"PC={playground.ProgramCounter} ACC={playground.Accumulator} RAM=[{playground.RamText}]");
            playground.ProgramCounter.ShouldBe(golden.ProgramCounter);
            playground.Accumulator.ShouldBe(golden.Accumulator);
            if (!isAdd) continue;

            var add = playground.PhotonicAlu.ShouldNotBeNull("a photonic ADD must run on the photonic ALU")
                .LastAddTrace.ShouldNotBeNull("the ADD must leave its trace");
            BitsOf(panel, "A").ShouldBe(Bits(add.A), "A0–A3 must equal this ADD's first operand");
            BitsOf(panel, "B").ShouldBe(Bits(add.B), "B0–B3 must equal this ADD's second operand");
            panel.Inputs.Single(i => i.PinName == "Cin").IsOn.ShouldBeFalse();
            add.Sum.ShouldBe((add.A + add.B) % 16);
            BitsOf(panel, "S", output: true).ShouldBe(Bits(add.Sum), "S = (A+B) mod 16");
            panel.Outputs.Single(o => o.PinName == "Cout").IsOne.ShouldBe(add.A + add.B >= 16,
                "Cout reads the carry of the 5-bit sum");
            var badge = canvas.LogicGateStates.Badges.Single(
                b => b.GroupName == SumBadgeGroupName && b.PinName == "Y");
            badge.IsOne.ShouldBe((add.Sum & 1) == 1,
                "the full-adder group's canvas badge must reflect the new evaluation");
            badgeBits.Add(badge.IsOne);
            driven.Add($"{add.A}+{add.B}");
        }

        golden.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step cap");
        playground.ErrorText.ShouldBeEmpty();
        driven.ShouldNotBeEmpty("the multiply sample executes photonic ADDs");
        if (stepTimes.Count > 0)
        {
            var median = stepTimes.OrderBy(t => t).ElementAt(stepTimes.Count / 2);
            median.ShouldBeLessThan(UiBudget,
                "the steady-state median step time must stay under the 100 ms UI budget (#1245)");
        }

        return (trace, driven, badgeBits);
    }

    /// <summary>The golden model's per-step trace of the same program, formatted as the VM renders it.</summary>
    private static List<string> GoldenModelTrace(string source)
    {
        var emulator = new IsaEmulator(new IsaAssembler().Assemble(source));
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

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

    /// <summary>The toggle (or indicator) bits of the pins named <paramref name="prefix"/>0–3, LSB first.</summary>
    private static bool[] BitsOf(LogicPanelViewModel panel, string prefix, bool output = false) =>
        Enumerable.Range(0, 4)
            .Select(bit => output
                ? panel.Outputs.Single(o => o.PinName == $"{prefix}{bit}").IsOne
                : panel.Inputs.Single(i => i.PinName == $"{prefix}{bit}").IsOn)
            .ToArray();

    /// <summary>The four bits of <paramref name="value"/>, LSB first.</summary>
    private static bool[] Bits(int value) =>
        Enumerable.Range(0, 4).Select(bit => ((value >> bit) & 1) == 1).ToArray();
}

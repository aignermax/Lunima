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
/// Rung-5 north star as one user journey (issue #1295): open the shipped Logic
/// Unit 4-bit example through the Home-Examples-tile path, Logic tab → Build
/// (real <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler"/>, real
/// hand-over through <see cref="BuiltLogicNetworkProvider"/>), then the DI-resolved
/// ISA playground runs the shipped "Mask &amp; invert" sample to HALT with the
/// photonic toggle on. Every AND and NOT step must execute photonically — AND on
/// the Y0–Y3 taps, NOT on the N0–N3 taps of the same network — and the per-step
/// trace (PC, ACC, RAM) plus the final machine state must equal the golden
/// model's run of the identical program.
/// </summary>
[Collection("LocalizationSingleton")]
public class Rung5LogicUnitMaskInvertJourneyTests
{
    private const string LogicUnitFileName = "Logic Gate Logic Unit 4-bit.lun";
    private const string MaskInvertSampleFileName = "mask-and-invert.asm";

    [Fact]
    public async Task LogicUnit_MaskAndInvert_RunsAndAndNotPhotonically_MatchingTheGoldenModel()
    {
        using var container = BuildContainer();
        var networkProvider = container.GetRequiredService<BuiltLogicNetworkProvider>();
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        var examples = new ExampleDesignsService().GetExamples();

        // Step 1: open the shipped Logic Unit 4-bit the way the Home Examples tile does.
        var canvas = new DesignCanvasViewModel();
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        (await fileOps.OpenDesignAsCopyAsync(
            examples.Single(e => Path.GetFileName(e.FilePath) == LogicUnitFileName).FilePath))
            .ShouldBeTrue();
        await fileOps.PostLoadRouting;

        // Step 2: Logic tab → Build; the network reaches the playground through the provider.
        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);
        networkProvider.Network.ShouldNotBeNull("a successful Logic Build hands the network to the playground");
        PhotonicAndAlu.Accepts(networkProvider.Network).ShouldBeTrue();
        PhotonicNotAlu.Accepts(networkProvider.Network, IsaAluSignalMap.CombinedLogicUnitNot)
            .ShouldBeTrue("the Logic Unit chip exposes the combined-map NOT taps N0–N3");

        // Step 3: assemble the shipped "Mask & invert" sample in the DI-resolved playground.
        var sample = playground.Samples.Single(s => s.FileName == MaskInvertSampleFileName);
        sample.DisplayName.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.SampleMaskAndInvert"));
        playground.SelectedSample = sample;
        playground.ProgramText.ShouldContain("mask-and-invert");
        playground.IsAssembled.ShouldBeTrue(playground.ErrorText);

        // Step 4: the toggle names both operations and switches both ALUs onto light.
        playground.IsPhotonicToggleEnabled.ShouldBeTrue(
            "the built Logic Unit network must enable the photonic toggle");
        playground.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicAndNotToggle"));
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue();
        playground.PhotonicAndAlu.ShouldNotBeNull();
        playground.PhotonicNotAlu.ShouldNotBeNull("NOT must run photonically through the combined map");
        playground.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAndNot"));

        // Step 5: run to HALT on the deterministic auto-step loop, recording the
        // photonic status after every tick — each AND and NOT step must leave its
        // photonic status line (a golden step leaves none).
        var photonicAndStatus = (string?)null;
        var photonicNotStatus = (string?)null;
        var andTemplate = LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAnd");
        var notTemplate = LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicNot");
        playground.ToggleRunCommand.Execute(null);
        playground.IsRunning.ShouldBeTrue("Run must start on the assembled program");
        var trace = new List<string>();
        while (playground.IsRunning && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            playground.AdvanceRunTick();
            trace.Add($"PC={playground.ProgramCounter} ACC={playground.Accumulator} RAM=[{playground.RamText}]");
            if (playground.PhotonicStatusText.Contains("1100") &&
                playground.PhotonicStatusText.Contains("1010"))
            {
                photonicAndStatus = playground.PhotonicStatusText;
            }
            else if (playground.PhotonicStatusText.Contains("1000") &&
                     playground.PhotonicStatusText.Contains("0111"))
            {
                photonicNotStatus = playground.PhotonicStatusText;
            }
        }

        playground.IsRunning.ShouldBeFalse("Mask & invert must halt within the step cap");
        playground.ErrorText.ShouldBeEmpty();
        playground.Accumulator.ShouldBe(7, "~(1100 & 1010) & 0xF = 0111");

        // Step 6: both operations ran on light — the status lines name them.
        photonicAndStatus.ShouldBe(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            andTemplate, "1100", "1010", "1000", networkProvider.Network!.Gates.Count),
            "the AND step must have executed photonically");
        photonicNotStatus.ShouldBe(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            notTemplate, "1000", "0111", networkProvider.Network.Gates.Count),
            "the NOT step must have executed photonically");

        // Step 7: the photonic run is bit-identical to the golden model's.
        var golden = GoldenModelRun(playground.ProgramText);
        trace.ShouldBe(golden.Trace,
            "every step of the photonic run must match the golden model");
        playground.RamText.ShouldBe(golden.RamText,
            "the final RAM of the photonic run must match the golden model");
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

    /// <summary>The golden model's run of the same program: per-step trace and final RAM, VM-formatted.</summary>
    private static (List<string> Trace, string RamText) GoldenModelRun(string source)
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
        return (trace, string.Join("  ", emulator.Ram));
    }
}

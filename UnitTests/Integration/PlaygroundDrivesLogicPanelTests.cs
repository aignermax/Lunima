using System.Diagnostics;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 visualizer seam (issue #1240): a photonic ADD in the ISA playground must
/// drive the Logic tab's A/B input toggles through the shared
/// <see cref="BuiltLogicNetworkProvider"/>, so the panel's live evaluation refreshes
/// the 0/1 badges and the named output chips exactly as if the user had clicked the
/// toggles. Golden-model ADDs must never touch the Logic tab, and one Run tick —
/// photonic ADD plus the panel refresh — must stay inside the 100 ms UI budget on
/// the shipped 344-gate adder. Playground and panel are DI-resolved over one shared
/// provider, like <see cref="Rung5IsaPlaygroundJourneyTests"/>.
/// </summary>
[Collection("LocalizationSingleton")]
public class PlaygroundDrivesLogicPanelTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const string AddProgram = "LOAD 5\nSTORE 0\nLOAD 3\nADD 0\nHALT";

    /// <summary>Generous bound on the 100 ms UI budget for one photonic Run tick.</summary>
    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (loads the design once).</summary>
    public PlaygroundDrivesLogicPanelTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task PhotonicAdd_DrivesLogicPanelInputs_AndOutputsShowTheSum()
    {
        using var container = BuildContainer();
        var logicPanel = await BuildLogicPanel(container);
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        playground.ProgramText = AddProgram;
        playground.AssembleCommand.Execute(null);
        playground.UsePhotonicAdder = true;
        playground.UsePhotonicAdder.ShouldBeTrue(playground.ErrorText);

        for (var i = 0; i < 4; i++)
        {
            playground.StepCommand.Execute(null);
        }

        playground.Accumulator.ShouldBe(8);
        InputBits(logicPanel, "A").ShouldBe(new[] { true, true, false, false },
            "A0–A3 read the ACC bits of 3 = 0011 (LSB first)");
        InputBits(logicPanel, "B").ShouldBe(new[] { true, false, true, false },
            "B0–B3 read the RAM[0] bits of 5 = 0101 (LSB first)");
        logicPanel.Inputs.Single(i => i.PinName == "Cin").IsOn.ShouldBeFalse();
        OutputBits(logicPanel, "S").ShouldBe(new[] { false, false, false, true },
            "S0–S3 read the sum 8 = 1000 (LSB first)");
        logicPanel.Outputs.Single(o => o.PinName == "Cout").IsOne.ShouldBeFalse();
    }

    [Fact]
    public async Task GoldenAdd_LeavesLogicPanelInputsUnchanged()
    {
        using var container = BuildContainer();
        var logicPanel = await BuildLogicPanel(container);
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        playground.ProgramText = AddProgram;
        playground.AssembleCommand.Execute(null);
        var before = logicPanel.Inputs.Select(i => i.IsOn).ToArray();

        for (var i = 0; i < 4; i++)
        {
            playground.StepCommand.Execute(null);
        }

        playground.Accumulator.ShouldBe(8, "the golden model still computes the ADD");
        logicPanel.Inputs.Select(i => i.IsOn).ShouldBe(before,
            "a golden-model ADD must never touch the Logic tab");
    }

    [Fact]
    public async Task RunTick_WithPhotonicAddAndPanelRefresh_StaysUnderUiBudget()
    {
        using var container = BuildContainer();
        var logicPanel = await BuildLogicPanel(container);
        var playground = container.GetRequiredService<IsaPlaygroundViewModel>();
        playground.ProgramText = AddProgram;
        playground.AssembleCommand.Execute(null);
        playground.UsePhotonicAdder = true;

        // Warm-up: the first photonic ADD pays JIT for the ALU, the panel's
        // re-evaluation and the badge push; the UI budget applies to steady state.
        for (var i = 0; i < 4; i++)
        {
            playground.StepCommand.Execute(null);
        }

        playground.ResetCommand.Execute(null);

        playground.ToggleRunCommand.Execute(null);
        playground.IsRunning.ShouldBeTrue();
        playground.AdvanceRunTick();
        playground.AdvanceRunTick();
        playground.AdvanceRunTick();

        var watch = Stopwatch.StartNew();
        playground.AdvanceRunTick();
        watch.Stop();

        playground.Accumulator.ShouldBe(8, "the fourth tick executed the photonic ADD");
        OutputBits(logicPanel, "S").ShouldBe(new[] { false, false, false, true },
            "the tick's panel refresh shows the sum 8 = 1000 (LSB first)");
        watch.Elapsed.ShouldBeLessThan(UiBudget,
            "one Run tick — photonic ADD plus the Logic panel refresh — must stay under the 100 ms UI budget");
    }

    /// <summary>The production DI wiring: playground + provider singleton + transient Logic panel.</summary>
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddIsaPlaygroundFeature();
        services.AddTransient<LogicPanelViewModel>();
        return services.BuildServiceProvider();
    }

    /// <summary>Builds the fixture's adder network through the real panel path (publishes to the provider).</summary>
    private async Task<LogicPanelViewModel> BuildLogicPanel(ServiceProvider container)
    {
        var logicPanel = container.GetRequiredService<LogicPanelViewModel>();
        logicPanel.Configure(_fixture.Canvas);
        await logicPanel.BuildNetworkCommand.ExecuteAsync(null);
        logicPanel.HasNetwork.ShouldBeTrue(logicPanel.StatusText);
        return logicPanel;
    }

    /// <summary>The toggle bits of the inputs named <paramref name="prefix"/>0–3, LSB first.</summary>
    private static bool[] InputBits(LogicPanelViewModel panel, string prefix) =>
        Enumerable.Range(0, 4)
            .Select(bit => panel.Inputs.Single(i => i.PinName == $"{prefix}{bit}").IsOn)
            .ToArray();

    /// <summary>The indicator bits of the outputs named <paramref name="prefix"/>0–3, LSB first.</summary>
    private static bool[] OutputBits(LogicPanelViewModel panel, string prefix) =>
        Enumerable.Range(0, 4)
            .Select(bit => panel.Outputs.Single(o => o.PinName == $"{prefix}{bit}").IsOne)
            .ToArray();
}
